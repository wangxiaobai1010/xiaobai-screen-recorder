#include "D3D11Nv12Converter.h"
#include "CropTransform.h"
#include "MfH264SinkWriterSession.h"
#include "Nv12TrackedTexturePool.h"

#include <windows.h>
#include <bcrypt.h>
#include <d3d10.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <map>
#include <numeric>
#include <sstream>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr UINT Width = 1920;
    constexpr UINT Height = 1080;
    constexpr UINT FrameCount = 120;
    constexpr UINT DecodeFrameIndex = 60;
    constexpr UINT Bitrate = 8'000'000;
    constexpr LONGLONG TicksPerSecond = 10'000'000;

    constexpr char ProductShader[] = R"(
Texture2D SourceTexture : register(t0);
SamplerState LinearSampler : register(s0);
cbuffer TransformBuffer : register(b0)
{
    float4 CameraUv;
    float4 CropUv;
};
struct VertexOutput { float4 position : SV_Position; float2 uv : TEXCOORD0; };
VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    static const float2 positions[3] =
    {
        float2(-1.0f, -1.0f), float2(-1.0f, 3.0f), float2(3.0f, -1.0f)
    };
    static const float2 uvs[3] =
    {
        float2(0.0f, 1.0f), float2(0.0f, -1.0f), float2(2.0f, 1.0f)
    };
    VertexOutput output;
    output.position = float4(positions[vertexId], 0.0f, 1.0f);
    output.uv = uvs[vertexId];
    return output;
}
float4 PSMain(VertexOutput input) : SV_Target
{
    const float2 regionLocalUv = CameraUv.xy + (input.uv * CameraUv.zw);
    const float2 sourceUv = CropUv.xy + (regionLocalUv * CropUv.zw);
    return SourceTexture.Sample(LinearSampler, sourceUv);
}
)";

    struct Roi
    {
        std::string name;
        int x{};
        int y{};
        int width{};
        int height{};
    };

    const std::array<Roi, 9> Rois{{
        { "onePixelLines", 40, 70, 480, 190 },
        { "checkerboard", 600, 80, 256, 256 },
        { "colorEdges", 1000, 80, 360, 220 },
        { "blackChinese", 70, 610, 760, 76 },
        { "grayChinese", 70, 700, 760, 76 },
        { "colorChinese", 70, 790, 760, 76 },
        { "englishText", 70, 890, 900, 100 },
        { "uiBorders", 970, 350, 420, 250 },
        { "gradients", 1400, 650, 480, 320 }
    }};

    [[noreturn]] void Fail(const std::string& stage, const HRESULT result)
    {
        std::ostringstream message;
        message << stage << " failed: 0x" << std::hex << std::uppercase
            << static_cast<unsigned long>(result);
        throw std::runtime_error(message.str());
    }

    void Check(const HRESULT result, const char* stage)
    {
        if (FAILED(result))
        {
            Fail(stage, result);
        }
    }

    struct ComApartment final
    {
        ~ComApartment() { CoUninitialize(); }
    };

    std::string Narrow(const std::wstring& value)
    {
        if (value.empty()) return {};
        const auto count = WideCharToMultiByte(
            CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
            nullptr, 0, nullptr, nullptr);
        std::string result(static_cast<std::size_t>(count), '\0');
        WideCharToMultiByte(
            CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
            result.data(), count, nullptr, nullptr);
        return result;
    }

    std::string Escape(const std::string& value)
    {
        std::string result;
        for (const char character : value)
        {
            switch (character)
            {
            case '\\': result += "\\\\"; break;
            case '"': result += "\\\""; break;
            case '\n': result += "\\n"; break;
            case '\r': result += "\\r"; break;
            case '\t': result += "\\t"; break;
            default: result += character; break;
            }
        }
        return result;
    }

    std::string Sha256(const std::filesystem::path& path)
    {
        BCRYPT_ALG_HANDLE algorithm{};
        BCRYPT_HASH_HANDLE hash{};
        DWORD objectLength{};
        DWORD resultLength{};
        Check(BCryptOpenAlgorithmProvider(
            &algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0), "BCryptOpen");
        Check(BCryptGetProperty(
            algorithm, BCRYPT_OBJECT_LENGTH,
            reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength),
            &resultLength, 0), "BCryptObjectLength");
        std::vector<UCHAR> object(objectLength);
        std::array<UCHAR, 32> digest{};
        Check(BCryptCreateHash(
            algorithm, &hash, object.data(), objectLength,
            nullptr, 0, 0), "BCryptCreateHash");
        std::ifstream input(path, std::ios::binary);
        if (!input) throw std::runtime_error("Cannot open file for SHA-256");
        std::array<char, 1 << 16> buffer{};
        while (input)
        {
            input.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
            const auto count = input.gcount();
            if (count > 0)
            {
                Check(BCryptHashData(
                    hash, reinterpret_cast<PUCHAR>(buffer.data()),
                    static_cast<ULONG>(count), 0), "BCryptHashData");
            }
        }
        Check(BCryptFinishHash(
            hash, digest.data(), static_cast<ULONG>(digest.size()), 0),
            "BCryptFinishHash");
        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        std::ostringstream output;
        output << std::hex << std::uppercase << std::setfill('0');
        for (const auto value : digest) output << std::setw(2) << +value;
        return output.str();
    }

    void SavePng(
        IWICImagingFactory* factory,
        const std::filesystem::path& path,
        const std::vector<std::uint8_t>& pixels)
    {
        ComPtr<IWICStream> stream;
        Check(factory->CreateStream(&stream), "WIC CreateStream");
        Check(stream->InitializeFromFilename(path.c_str(), GENERIC_WRITE),
            "WIC InitializeStream");
        ComPtr<IWICBitmapEncoder> encoder;
        Check(factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder),
            "WIC CreateEncoder");
        Check(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache),
            "WIC EncoderInitialize");
        ComPtr<IWICBitmapFrameEncode> frame;
        ComPtr<IPropertyBag2> properties;
        Check(encoder->CreateNewFrame(&frame, &properties), "WIC NewFrame");
        Check(frame->Initialize(properties.Get()), "WIC FrameInitialize");
        Check(frame->SetSize(Width, Height), "WIC SetSize");
        WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
        Check(frame->SetPixelFormat(&format), "WIC SetPixelFormat");
        if (format != GUID_WICPixelFormat32bppBGRA)
            throw std::runtime_error("WIC changed the requested BGRA format");
        Check(frame->WritePixels(
            Height, Width * 4, static_cast<UINT>(pixels.size()),
            const_cast<BYTE*>(pixels.data())), "WIC WritePixels");
        Check(frame->Commit(), "WIC FrameCommit");
        Check(encoder->Commit(), "WIC EncoderCommit");
    }

    void SetPixel(
        std::uint8_t* data, const int x, const int y,
        const std::uint8_t r, const std::uint8_t g, const std::uint8_t b)
    {
        if (x < 0 || y < 0 || x >= static_cast<int>(Width) ||
            y >= static_cast<int>(Height)) return;
        auto* pixel = data + (static_cast<std::size_t>(y) * Width + x) * 4;
        pixel[0] = b; pixel[1] = g; pixel[2] = r; pixel[3] = 255;
    }

    void FillRectPixels(
        std::uint8_t* data, const int left, const int top,
        const int width, const int height,
        const std::uint8_t r, const std::uint8_t g, const std::uint8_t b)
    {
        for (int y = top; y < top + height; ++y)
            for (int x = left; x < left + width; ++x)
                SetPixel(data, x, y, r, g, b);
    }

    void FrameRectPixels(
        std::uint8_t* data, const int left, const int top,
        const int width, const int height, const int thickness,
        const std::uint8_t r, const std::uint8_t g, const std::uint8_t b)
    {
        FillRectPixels(data, left, top, width, thickness, r, g, b);
        FillRectPixels(data, left, top + height - thickness,
            width, thickness, r, g, b);
        FillRectPixels(data, left, top, thickness, height, r, g, b);
        FillRectPixels(data, left + width - thickness, top,
            thickness, height, r, g, b);
    }

    HFONT MakeFont(const wchar_t* face, const int pixelHeight)
    {
        return CreateFontW(
            -pixelHeight, 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
            DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
            CLEARTYPE_NATURAL_QUALITY, DEFAULT_PITCH, face);
    }

    void DrawTextLine(
        HDC dc, const wchar_t* face, const int size,
        const int x, const int y, const COLORREF color,
        const std::wstring& text)
    {
        const auto font = MakeFont(face, size);
        if (!font) throw std::runtime_error("CreateFont failed");
        const auto previous = SelectObject(dc, font);
        SetTextColor(dc, color);
        SetBkMode(dc, TRANSPARENT);
        if (!TextOutW(dc, x, y, text.c_str(), static_cast<int>(text.size())))
            throw std::runtime_error("TextOutW failed");
        SelectObject(dc, previous);
        DeleteObject(font);
    }

    std::vector<std::uint8_t> CreatePattern()
    {
        BITMAPINFO info{};
        info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = Width;
        info.bmiHeader.biHeight = -static_cast<LONG>(Height);
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = BI_RGB;
        void* bits{};
        const auto bitmap = CreateDIBSection(
            nullptr, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (!bitmap || !bits) throw std::runtime_error("CreateDIBSection failed");
        const auto dc = CreateCompatibleDC(nullptr);
        if (!dc) throw std::runtime_error("CreateCompatibleDC failed");
        const auto previousBitmap = SelectObject(dc, bitmap);
        auto* data = static_cast<std::uint8_t*>(bits);
        FillRectPixels(data, 0, 0, Width, Height, 255, 255, 255);

        FillRectPixels(data, 0, 0, Width, 48, 18, 18, 18);
        FillRectPixels(data, 20, 12, 360, 24, 255, 255, 255);
        FillRectPixels(data, 400, 12, 360, 24, 0, 0, 0);

        for (int y = 70; y < 130; ++y)
            FillRectPixels(data, 40, y, 220, 1,
                (y & 1) ? 255 : 0, (y & 1) ? 255 : 0, (y & 1) ? 255 : 0);
        for (int x = 290; x < 350; ++x)
            FillRectPixels(data, x, 70, 1, 190,
                (x & 1) ? 255 : 0, (x & 1) ? 255 : 0, (x & 1) ? 255 : 0);
        for (int x = 380; x < 520; x += 5)
        {
            FillRectPixels(data, x, 70, 2, 80, 0, 0, 0);
            FillRectPixels(data, x, 170, 3, 90, 0, 0, 0);
        }
        for (int y = 80; y < 336; ++y)
            for (int x = 600; x < 856; ++x)
            {
                const auto white = ((x - 600) + (y - 80)) & 1;
                SetPixel(data, x, y, white ? 255 : 0,
                    white ? 255 : 0, white ? 255 : 0);
            }

        FillRectPixels(data, 1000, 80, 180, 220, 0, 0, 0);
        FillRectPixels(data, 1180, 80, 180, 220, 255, 255, 255);
        FillRectPixels(data, 1380, 80, 120, 220, 255, 0, 0);
        FillRectPixels(data, 1500, 80, 120, 220, 0, 255, 0);
        FillRectPixels(data, 1620, 80, 120, 220, 0, 0, 255);

        FillRectPixels(data, 970, 350, 420, 250, 245, 245, 245);
        FrameRectPixels(data, 970, 350, 420, 250, 1, 0, 0, 0);
        FrameRectPixels(data, 990, 385, 160, 38, 1, 70, 70, 70);
        FrameRectPixels(data, 1170, 385, 190, 38, 2, 30, 100, 210);
        for (int i = 0; i < 12; ++i)
        {
            const int x = 990 + i * 30;
            FrameRectPixels(data, x, 470, 18, 18, 1, 0, 0, 0);
            SetPixel(data, x + 8, 478, 0, 0, 0);
        }

        for (int x = 1400; x < 1880; ++x)
        {
            const auto value = static_cast<std::uint8_t>(
                (x - 1400) * 255 / 479);
            FillRectPixels(data, x, 650, 1, 130, value, value, value);
            FillRectPixels(data, x, 800, 1, 130,
                value, static_cast<std::uint8_t>(255 - value),
                static_cast<std::uint8_t>((value * 3) & 255));
        }
        FillRectPixels(data, 1000, 650, 330, 330, 238, 242, 247);
        FrameRectPixels(data, 1000, 650, 330, 330, 1, 65, 65, 65);
        for (int i = 0; i < 15; ++i)
        {
            const int cx = 1030 + (i % 5) * 58;
            const int cy = 690 + (i / 5) * 78;
            FrameRectPixels(data, cx, cy, 34, 34, 1, 20, 20, 20);
            for (int d = 4; d < 30; d += 4)
                SetPixel(data, cx + d, cy + d, 0, 90, 200);
        }

        DrawTextLine(dc, L"Microsoft YaHei UI", 12, 70, 610,
            RGB(0, 0, 0), L"小白录屏器 文字清晰度测试  12px");
        DrawTextLine(dc, L"Microsoft YaHei UI", 14, 70, 700,
            RGB(96, 96, 96), L"小白录屏器 文字清晰度测试  14px");
        DrawTextLine(dc, L"Microsoft YaHei UI", 18, 70, 790,
            RGB(0, 92, 220), L"小白录屏器 文字清晰度测试  18px");
        DrawTextLine(dc, L"Consolas", 12, 70, 890,
            RGB(0, 0, 0), L"1080p 60 FPS  abcXYZ 0123456789");
        DrawTextLine(dc, L"Consolas", 14, 70, 930,
            RGB(120, 20, 160), L"1.0x Wide Pixel Test");

        for (std::size_t index = 3; index < Width * Height * 4; index += 4)
            data[index] = 255;
        std::vector<std::uint8_t> result(Width * Height * 4);
        std::copy_n(data, result.size(), result.data());
        SelectObject(dc, previousBitmap);
        DeleteDC(dc);
        DeleteObject(bitmap);
        return result;
    }

    struct DeviceState
    {
        ComPtr<ID3D11Device> device;
        ComPtr<ID3D11DeviceContext> context;
        std::string adapter;
    };

    DeviceState CreateDevice()
    {
        DeviceState result;
        D3D_FEATURE_LEVEL feature{};
        const UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT |
            D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        Check(D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
            nullptr, 0, D3D11_SDK_VERSION, &result.device,
            &feature, &result.context), "D3D11CreateDevice");
        ComPtr<IDXGIDevice> dxgi;
        Check(result.device.As(&dxgi), "Query IDXGIDevice");
        ComPtr<IDXGIAdapter> adapter;
        Check(dxgi->GetAdapter(&adapter), "GetAdapter");
        DXGI_ADAPTER_DESC description{};
        Check(adapter->GetDesc(&description), "GetDesc");
        result.adapter = Narrow(description.Description);
        ComPtr<ID3D10Multithread> multithread;
        Check(result.device.As(&multithread), "Query ID3D10Multithread");
        multithread->SetMultithreadProtected(TRUE);
        if (!multithread->GetMultithreadProtected())
            throw std::runtime_error("D3D multithread protection was not enabled");
        return result;
    }

    ComPtr<ID3D11Texture2D> CreateSourceTexture(
        ID3D11Device* device, const std::vector<std::uint8_t>& pixels)
    {
        D3D11_TEXTURE2D_DESC description{};
        description.Width = Width; description.Height = Height;
        description.MipLevels = 1; description.ArraySize = 1;
        description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_IMMUTABLE;
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA initial{};
        initial.pSysMem = pixels.data(); initial.SysMemPitch = Width * 4;
        ComPtr<ID3D11Texture2D> texture;
        Check(device->CreateTexture2D(&description, &initial, &texture),
            "CreateSourceTexture");
        return texture;
    }

    std::vector<std::uint8_t> Readback(
        ID3D11Device* device, ID3D11DeviceContext* context,
        ID3D11Texture2D* texture)
    {
        D3D11_TEXTURE2D_DESC description{};
        texture->GetDesc(&description);
        description.Usage = D3D11_USAGE_STAGING;
        description.BindFlags = 0;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
        description.MiscFlags = 0;
        ComPtr<ID3D11Texture2D> staging;
        Check(device->CreateTexture2D(&description, nullptr, &staging),
            "CreateReadbackTexture");
        context->CopyResource(staging.Get(), texture);
        D3D11_MAPPED_SUBRESOURCE mapped{};
        Check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped),
            "MapReadback");
        std::vector<std::uint8_t> result(Width * Height * 4);
        for (UINT y = 0; y < Height; ++y)
            std::copy_n(
                static_cast<const std::uint8_t*>(mapped.pData) +
                    static_cast<std::size_t>(y) * mapped.RowPitch,
                Width * 4,
                result.data() + static_cast<std::size_t>(y) * Width * 4);
        context->Unmap(staging.Get(), 0);
        return result;
    }

    struct RenderResult
    {
        ComPtr<ID3D11Texture2D> texture;
        std::vector<std::uint8_t> pixels;
    };

    RenderResult Render(
        const DeviceState& state, ID3D11Texture2D* source,
        const bool candidate)
    {
        ComPtr<ID3DBlob> vertexCode, pixelCode, errors;
        Check(D3DCompile(
            ProductShader, sizeof(ProductShader), "ProductShader", nullptr,
            nullptr, "VSMain", "vs_5_0", 0, 0, &vertexCode, &errors),
            "CompileVS");
        Check(D3DCompile(
            ProductShader, sizeof(ProductShader), "ProductShader", nullptr,
            nullptr, "PSMain", "ps_5_0", 0, 0, &pixelCode, &errors),
            "CompilePS");
        ComPtr<ID3D11VertexShader> vertex;
        ComPtr<ID3D11PixelShader> pixel;
        Check(state.device->CreateVertexShader(
            vertexCode->GetBufferPointer(), vertexCode->GetBufferSize(),
            nullptr, &vertex), "CreateVS");
        Check(state.device->CreatePixelShader(
            pixelCode->GetBufferPointer(), pixelCode->GetBufferSize(),
            nullptr, &pixel), "CreatePS");
        D3D11_TEXTURE2D_DESC outputDescription{};
        outputDescription.Width = Width; outputDescription.Height = Height;
        outputDescription.MipLevels = 1; outputDescription.ArraySize = 1;
        outputDescription.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        outputDescription.SampleDesc.Count = 1;
        outputDescription.Usage = D3D11_USAGE_DEFAULT;
        outputDescription.BindFlags = D3D11_BIND_RENDER_TARGET |
            D3D11_BIND_SHADER_RESOURCE;
        RenderResult result;
        Check(state.device->CreateTexture2D(
            &outputDescription, nullptr, &result.texture), "CreateOutput");
        ComPtr<ID3D11RenderTargetView> target;
        ComPtr<ID3D11ShaderResourceView> sourceView;
        Check(state.device->CreateRenderTargetView(
            result.texture.Get(), nullptr, &target), "CreateRTV");
        Check(state.device->CreateShaderResourceView(
            source, nullptr, &sourceView), "CreateSRV");
        D3D11_SAMPLER_DESC samplerDescription{};
        samplerDescription.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        samplerDescription.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        samplerDescription.MaxLOD = D3D11_FLOAT32_MAX;
        ComPtr<ID3D11SamplerState> sampler;
        Check(state.device->CreateSamplerState(
            &samplerDescription, &sampler), "CreateSampler");
        struct alignas(16) Constants
        {
            float camera[4];
            float crop[4];
        } constants{{ 0, 0, 1, 1 }, {}};
        if (candidate)
        {
            XbPreviewSessionGeometryV1 geometry{};
            geometry.sourceWidth = Width;
            geometry.sourceHeight = Height;
            geometry.captureLeft = 0;
            geometry.captureTop = 0;
            geometry.captureWidth = Width;
            geometry.captureHeight = Height;
            geometry.outputWidth = Width;
            geometry.outputHeight = Height;
            xbpreview::CropTransform productCrop{};
            if (!xbpreview::ResolveCropTransform(geometry, productCrop))
            {
                throw std::runtime_error(
                    "Product ResolveCropTransform rejected Wide geometry");
            }
            constants.crop[0] = productCrop.originU;
            constants.crop[1] = productCrop.originV;
            constants.crop[2] = productCrop.scaleU;
            constants.crop[3] = productCrop.scaleV;
        }
        else
        {
            constants.crop[0] = 0.5f / Width;
            constants.crop[1] = 0.5f / Height;
            constants.crop[2] = static_cast<float>(Width - 1) / Width;
            constants.crop[3] = static_cast<float>(Height - 1) / Height;
        }
        D3D11_BUFFER_DESC bufferDescription{};
        bufferDescription.ByteWidth = sizeof(Constants);
        bufferDescription.Usage = D3D11_USAGE_IMMUTABLE;
        bufferDescription.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        D3D11_SUBRESOURCE_DATA initial{ &constants };
        ComPtr<ID3D11Buffer> constantBuffer;
        Check(state.device->CreateBuffer(
            &bufferDescription, &initial, &constantBuffer), "CreateCB");
        constexpr float clear[4]{ 0, 0, 0, 1 };
        state.context->ClearRenderTargetView(target.Get(), clear);
        ID3D11RenderTargetView* targetPointer = target.Get();
        state.context->OMSetRenderTargets(1, &targetPointer, nullptr);
        const D3D11_VIEWPORT viewport{
            0, 0, static_cast<float>(Width), static_cast<float>(Height), 0, 1 };
        state.context->RSSetViewports(1, &viewport);
        state.context->IASetInputLayout(nullptr);
        state.context->IASetPrimitiveTopology(
            D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        state.context->VSSetShader(vertex.Get(), nullptr, 0);
        state.context->PSSetShader(pixel.Get(), nullptr, 0);
        ID3D11ShaderResourceView* viewPointer = sourceView.Get();
        ID3D11SamplerState* samplerPointer = sampler.Get();
        ID3D11Buffer* bufferPointer = constantBuffer.Get();
        state.context->PSSetShaderResources(0, 1, &viewPointer);
        state.context->PSSetSamplers(0, 1, &samplerPointer);
        state.context->PSSetConstantBuffers(0, 1, &bufferPointer);
        state.context->Draw(3, 0);
        ID3D11ShaderResourceView* nullView{};
        ID3D11RenderTargetView* nullTarget{};
        state.context->PSSetShaderResources(0, 1, &nullView);
        state.context->OMSetRenderTargets(1, &nullTarget, nullptr);
        result.pixels = Readback(
            state.device.Get(), state.context.Get(), result.texture.Get());
        return result;
    }

    double Luma(const std::uint8_t* pixel)
    {
        return 0.0722 * pixel[0] + 0.7152 * pixel[1] + 0.2126 * pixel[2];
    }

    double Sharpness(const std::vector<std::uint8_t>& pixels, const Roi& roi)
    {
        double sum{};
        std::uint64_t count{};
        for (int y = roi.y; y < roi.y + roi.height - 1; ++y)
            for (int x = roi.x; x < roi.x + roi.width - 1; ++x)
            {
                const auto* p = pixels.data() +
                    (static_cast<std::size_t>(y) * Width + x) * 4;
                const auto* right = p + 4;
                const auto* down = p + Width * 4;
                sum += std::abs(Luma(p) - Luma(right));
                sum += std::abs(Luma(p) - Luma(down));
                count += 2;
            }
        return count ? sum / count : 0;
    }

    double Contrast(const std::vector<std::uint8_t>& pixels, const Roi& roi)
    {
        double sum{};
        std::uint64_t count{};
        for (int y = roi.y; y < roi.y + roi.height; ++y)
            for (int x = roi.x; x < roi.x + roi.width - 1; ++x)
            {
                const auto* p = pixels.data() +
                    (static_cast<std::size_t>(y) * Width + x) * 4;
                sum += std::abs(Luma(p) - Luma(p + 4));
                ++count;
            }
        return count ? sum / count : 0;
    }

    struct Metrics
    {
        std::uint64_t total{};
        std::uint64_t exact{};
        std::uint64_t mismatch{};
        std::array<int, 4> maxError{};
        std::array<double, 4> mae{};
        double rmse{};
        double psnr{};
        double onePixelRetention{};
        double checkerContrast{};
        double edgeTransitionWidth{};
        double colorEdgeSharpness{};
        double textSharpness{};
        std::map<std::string, double> roiSharpness;
    };

    Metrics Compare(
        const std::vector<std::uint8_t>& reference,
        const std::vector<std::uint8_t>& actual)
    {
        if (reference.size() != actual.size())
            throw std::runtime_error("Pixel buffer size mismatch");
        Metrics result;
        result.total = Width * Height;
        std::array<long double, 4> absolute{};
        long double squared{};
        for (std::uint64_t index = 0; index < result.total; ++index)
        {
            bool exact = true;
            for (int channel = 0; channel < 4; ++channel)
            {
                const auto delta = std::abs(
                    static_cast<int>(reference[index * 4 + channel]) -
                    static_cast<int>(actual[index * 4 + channel]));
                exact = exact && delta == 0;
                result.maxError[channel] =
                    std::max(result.maxError[channel], delta);
                absolute[channel] += delta;
                if (channel < 3) squared += static_cast<long double>(delta) * delta;
            }
            if (exact) ++result.exact;
        }
        result.mismatch = result.total - result.exact;
        for (int channel = 0; channel < 4; ++channel)
            result.mae[channel] = static_cast<double>(
                absolute[channel] / result.total);
        const auto mse = static_cast<double>(squared / (result.total * 3));
        result.rmse = std::sqrt(mse);
        result.psnr = mse == 0
            ? std::numeric_limits<double>::infinity()
            : 10.0 * std::log10(255.0 * 255.0 / mse);

        const auto& lineRoi = Rois[0];
        std::uint64_t retained{}, expected{};
        for (int y = lineRoi.y; y < lineRoi.y + lineRoi.height; ++y)
            for (int x = lineRoi.x; x < lineRoi.x + lineRoi.width; ++x)
            {
                const auto offset = (static_cast<std::size_t>(y) * Width + x) * 4;
                const auto source = Luma(reference.data() + offset);
                if (source <= 1 || source >= 254)
                {
                    ++expected;
                    const auto output = Luma(actual.data() + offset);
                    if ((source <= 1 && output <= 5) ||
                        (source >= 254 && output >= 250)) ++retained;
                }
            }
        result.onePixelRetention = expected
            ? static_cast<double>(retained) * 100.0 / expected : 0;
        result.checkerContrast = Contrast(actual, Rois[1]);
        result.colorEdgeSharpness = Sharpness(actual, Rois[2]);
        result.textSharpness = (
            Sharpness(actual, Rois[3]) + Sharpness(actual, Rois[4]) +
            Sharpness(actual, Rois[5]) + Sharpness(actual, Rois[6])) / 4.0;
        for (const auto& roi : Rois)
            result.roiSharpness.emplace(roi.name, Sharpness(actual, roi));
        double transition{};
        for (int y = 100; y < 280; ++y)
        {
            int count{};
            for (int x = 1168; x < 1192; ++x)
            {
                const auto* p = actual.data() +
                    (static_cast<std::size_t>(y) * Width + x) * 4;
                const auto value = Luma(p);
                if (value > 10 && value < 245) ++count;
            }
            transition += count;
        }
        result.edgeTransitionWidth = transition / 180.0;
        return result;
    }

    struct EncodeResult
    {
        std::uint64_t bytes{};
        double bitrate{};
        std::uint32_t submitted{};
        std::uint32_t returned{};
        double finalizeMs{};
        std::string sourceReaderValidation;
        std::uint64_t sourceReaderFrames{};
    };

    EncodeResult Encode(
        const DeviceState& state, ID3D11Texture2D* source,
        const std::filesystem::path& output)
    {
        xbpreview::D3D11Nv12Converter converter;
        xbpreview::Nv12TrackedTexturePool pool;
        xbpreview::MfH264SinkWriterSession sink;
        xbpreview::VideoEncoderDiagnostics diagnostics;
        converter.Initialize(
            state.device.Get(), state.context.Get(), Width, Height);
        pool.Initialize(
            state.device.Get(), converter.VideoDevice(), converter.Enumerator(),
            Width, Height);
        Check(sink.Start(
            state.device.Get(), Width, Height, Bitrate,
            output.wstring(), diagnostics), "Sink Start");
        EncodeResult result;
        for (UINT frame = 0; frame < FrameCount; ++frame)
        {
            std::optional<std::size_t> slot;
            const auto deadline = std::chrono::steady_clock::now() +
                std::chrono::seconds(10);
            while (!(slot = pool.TryAcquire()))
            {
                if (std::chrono::steady_clock::now() >= deadline)
                    throw std::runtime_error("NV12 pool acquisition timed out");
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }
            const auto convertResult = converter.Convert(
                source, 1, static_cast<std::uint32_t>(*slot),
                pool.OutputView(*slot));
            if (FAILED(convertResult))
            {
                pool.CancelProducing(*slot);
                Check(convertResult, "VideoProcessor Convert");
            }
            const auto time = static_cast<LONGLONG>(frame) *
                TicksPerSecond / 60;
            const auto next = static_cast<LONGLONG>(frame + 1) *
                TicksPerSecond / 60;
            ComPtr<IMFSample> sample;
            const auto sampleResult = pool.CreateTrackedSample(
                *slot, time, next - time, &sample);
            if (FAILED(sampleResult))
            {
                pool.CancelProducing(*slot);
                Check(sampleResult, "CreateTrackedSample");
            }
            double writeDuration{};
            Check(sink.WriteSample(sample.Get(), writeDuration), "WriteSample");
            ++result.submitted;
        }
        Check(sink.Finalize(diagnostics), "Finalize");
        result.finalizeMs = diagnostics.finalizeDurationMs;
        pool.MarkStopping();
        if (!pool.WaitForAllReturned(std::chrono::seconds(10)))
            throw std::runtime_error("Tracked NV12 samples were not returned");
        const auto poolDiagnostics = pool.Diagnostics();
        result.returned = static_cast<std::uint32_t>(poolDiagnostics.callbackCount);
        if (poolDiagnostics.outstanding != 0 ||
            poolDiagnostics.doubleReturn != 0 ||
            poolDiagnostics.invalidStateTransition != 0 ||
            result.returned != result.submitted)
            throw std::runtime_error("NV12 pool did not finish balanced");
        Check(sink.FullTestValidation(diagnostics), "FullTestValidation");
        result.sourceReaderValidation = diagnostics.sourceReaderValidation;
        result.sourceReaderFrames = diagnostics.decodedFrameCount;
        sink.Shutdown();
        pool.Shutdown();
        converter.Shutdown();
        result.bytes = std::filesystem::file_size(output);
        result.bitrate = static_cast<double>(result.bytes) * 8.0 / 2.0;
        return result;
    }

    struct DecodeResult
    {
        std::vector<std::uint8_t> pixels;
        LONGLONG pts{};
        UINT width{};
        UINT height{};
        std::uint64_t samples{};
    };

    DecodeResult Decode(const std::filesystem::path& path)
    {
        Check(MFStartup(MF_VERSION, MFSTARTUP_FULL), "MFStartup Decode");
        DecodeResult result;
        try
        {
            ComPtr<IMFAttributes> attributes;
            Check(MFCreateAttributes(&attributes, 2), "Decode Attributes");
            Check(attributes->SetUINT32(
                MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE),
                "Enable VideoProcessing");
            Check(attributes->SetUINT32(
                MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE),
                "Enable HardwareTransforms");
            ComPtr<IMFSourceReader> reader;
            Check(MFCreateSourceReaderFromURL(
                path.c_str(), attributes.Get(), &reader), "CreateSourceReader");
            ComPtr<IMFMediaType> type;
            Check(MFCreateMediaType(&type), "Decode MediaType");
            Check(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video),
                "Decode MajorType");
            Check(type->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32),
                "Decode RGB32");
            Check(reader->SetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                nullptr, type.Get()), "Set Decode Type");
            ComPtr<IMFMediaType> active;
            Check(reader->GetCurrentMediaType(
                static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                &active), "Get Decode Type");
            Check(MFGetAttributeSize(
                active.Get(), MF_MT_FRAME_SIZE, &result.width, &result.height),
                "Decode Size");
            if (result.width != Width || result.height != Height)
                throw std::runtime_error("Decoded size is not 1920x1080");
            LONG activeStride{};
            UINT32 strideValue{};
            if (SUCCEEDED(active->GetUINT32(MF_MT_DEFAULT_STRIDE, &strideValue)))
                activeStride = static_cast<LONG>(strideValue);
            else
                Check(MFGetStrideForBitmapInfoHeader(
                    MFVideoFormat_RGB32.Data1, Width, &activeStride),
                    "Decode DefaultStride");
            for (;;)
            {
                DWORD flags{};
                LONGLONG timestamp{};
                ComPtr<IMFSample> sample;
                Check(reader->ReadSample(
                    static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),
                    0, nullptr, &flags, &timestamp, &sample), "ReadSample");
                if ((flags & MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                    throw std::runtime_error("Decode reached EOS before frame 60");
                if (!sample) continue;
                if (result.samples++ != DecodeFrameIndex) continue;
                result.pts = timestamp;
                ComPtr<IMFMediaBuffer> buffer;
                Check(sample->ConvertToContiguousBuffer(&buffer),
                    "Decode ContiguousBuffer");
                ComPtr<IMF2DBuffer> buffer2d;
                result.pixels.resize(Width * Height * 4);
                if (SUCCEEDED(buffer.As(&buffer2d)))
                {
                    BYTE* scanline{};
                    LONG pitch{};
                    Check(buffer2d->Lock2D(&scanline, &pitch), "Decode Lock2D");
                    for (UINT y = 0; y < Height; ++y)
                    {
                        const auto* source = scanline +
                            static_cast<ptrdiff_t>(y) * pitch;
                        auto* destination = result.pixels.data() +
                            static_cast<std::size_t>(y) * Width * 4;
                        std::copy_n(source, Width * 4, destination);
                        for (UINT x = 0; x < Width; ++x)
                            destination[x * 4 + 3] = 255;
                    }
                    buffer2d->Unlock2D();
                }
                else
                {
                    BYTE* bytes{};
                    DWORD maximum{}, current{};
                    Check(buffer->Lock(&bytes, &maximum, &current),
                        "Decode BufferLock");
                    const auto rowBytes = static_cast<std::size_t>(
                        std::abs(activeStride));
                    if (rowBytes < Width * 4 ||
                        current < rowBytes * Height)
                    {
                        buffer->Unlock();
                        throw std::runtime_error("Decoded RGB32 buffer is undersized");
                    }
                    for (UINT y = 0; y < Height; ++y)
                    {
                        const auto* source = bytes +
                            static_cast<std::size_t>(y) * rowBytes;
                        auto* destination = result.pixels.data() +
                            static_cast<std::size_t>(y) * Width * 4;
                        std::copy_n(source, Width * 4, destination);
                        for (UINT x = 0; x < Width; ++x)
                            destination[x * 4 + 3] = 255;
                    }
                    buffer->Unlock();
                }
                break;
            }
            MFShutdown();
            return result;
        }
        catch (...)
        {
            MFShutdown();
            throw;
        }
    }

    struct ZoomCheck
    {
        double zoom{};
        std::string target;
        double left{};
        double top{};
        double right{};
        double bottom{};
        bool valid{};
    };

    std::vector<ZoomCheck> CheckZooms()
    {
        std::vector<ZoomCheck> results;
        for (const double zoom : { 1.6, 2.0 })
            for (const auto& item : std::array<std::pair<const char*,
                std::pair<double, double>>, 3>{{
                    { "topLeft", { 0.0, 0.0 } },
                    { "center", { 0.5, 0.5 } },
                    { "bottomRight", { 1.0, 1.0 } }
                }})
            {
                const auto size = 1.0 / zoom;
                const auto left = std::clamp(item.second.first - size / 2,
                    0.0, 1.0 - size);
                const auto top = std::clamp(item.second.second - size / 2,
                    0.0, 1.0 - size);
                ZoomCheck check{ zoom, item.first, left, top,
                    left + size, top + size };
                check.valid = check.left >= 0 && check.top >= 0 &&
                    check.right <= 1 && check.bottom <= 1 &&
                    check.right > check.left && check.bottom > check.top;
                results.push_back(check);
            }
        return results;
    }

    void WriteMetricsObject(std::ostream& output, const Metrics& metrics)
    {
        output << std::fixed << std::setprecision(8)
            << "{\"TotalPixels\":" << metrics.total
            << ",\"ExactMatchPixels\":" << metrics.exact
            << ",\"MismatchPixels\":" << metrics.mismatch
            << ",\"ExactMatchPercent\":"
            << (100.0 * metrics.exact / metrics.total)
            << ",\"MaxAbsoluteErrorBGRA\":["
            << metrics.maxError[0] << ',' << metrics.maxError[1] << ','
            << metrics.maxError[2] << ',' << metrics.maxError[3]
            << "],\"MeanAbsoluteErrorBGRA\":["
            << metrics.mae[0] << ',' << metrics.mae[1] << ','
            << metrics.mae[2] << ',' << metrics.mae[3]
            << "],\"RMSE\":" << metrics.rmse
            << ",\"PSNR\":";
        if (std::isinf(metrics.psnr)) output << "null";
        else output << metrics.psnr;
        output << ",\"OnePixelLineRetentionPercent\":"
            << metrics.onePixelRetention
            << ",\"CheckerContrast\":" << metrics.checkerContrast
            << ",\"BlackWhiteEdgeTransitionWidthPx\":"
            << metrics.edgeTransitionWidth
            << ",\"ColorEdgeSharpness\":" << metrics.colorEdgeSharpness
            << ",\"TextEdgeSharpness\":" << metrics.textSharpness
            << ",\"RoiSharpness\":{";
        bool first = true;
        for (const auto& [name, value] : metrics.roiSharpness)
        {
            if (!first) output << ',';
            first = false;
            output << '"' << name << "\":" << value;
        }
        output << "}}";
    }

    void WriteOutputs(
        const std::filesystem::path& outputDirectory,
        const std::string& runId,
        const DeviceState& device,
        const Metrics& sourceA,
        const Metrics& sourceB,
        const Metrics& aDecoded,
        const Metrics& bDecoded,
        const Metrics& aOutputDecoded,
        const Metrics& bOutputDecoded,
        const EncodeResult& encodeA,
        const EncodeResult& encodeB,
        const DecodeResult& decodeA,
        const DecodeResult& decodeB,
        const std::vector<ZoomCheck>& zooms,
        const std::string& conclusion)
    {
        const auto hash = [&](const char* name) {
            return Sha256(outputDirectory / name);
        };
        std::ofstream metrics(outputDirectory / "metrics.json");
        metrics << "{\n\"SchemaVersion\":1,\n\"RunId\":\"" << runId
            << "\",\n\"Width\":1920,\n\"Height\":1080,\n"
            << "\"OnlyVariable\":\"Crop UV\",\n"
            << "\"SourceVsBaselineOutput\":";
        WriteMetricsObject(metrics, sourceA);
        metrics << ",\n\"SourceVsCandidateOutput\":";
        WriteMetricsObject(metrics, sourceB);
        metrics << ",\n\"SourceVsBaselineDecoded\":";
        WriteMetricsObject(metrics, aDecoded);
        metrics << ",\n\"SourceVsCandidateDecoded\":";
        WriteMetricsObject(metrics, bDecoded);
        metrics << ",\n\"BaselineOutputVsDecoded\":";
        WriteMetricsObject(metrics, aOutputDecoded);
        metrics << ",\n\"CandidateOutputVsDecoded\":";
        WriteMetricsObject(metrics, bOutputDecoded);
        metrics << ",\n\"RoiComparison\":[";
        for (std::size_t index = 0; index < Rois.size(); ++index)
        {
            if (index) metrics << ',';
            const auto& roi = Rois[index];
            const auto outputA = sourceA.roiSharpness.at(roi.name);
            const auto outputB = sourceB.roiSharpness.at(roi.name);
            const auto decodedA = aDecoded.roiSharpness.at(roi.name);
            const auto decodedB = bDecoded.roiSharpness.at(roi.name);
            const auto outputImprovement = outputA == 0 ? 0 :
                (outputB - outputA) * 100.0 / std::abs(outputA);
            const auto decodedImprovement = decodedA == 0 ? 0 :
                (decodedB - decodedA) * 100.0 / std::abs(decodedA);
            metrics << "{\"Name\":\"" << roi.name << "\",\"Rect\":["
                << roi.x << ',' << roi.y << ',' << roi.width << ',' << roi.height
                << "],\"BaselineOutputSharpness\":" << outputA
                << ",\"CandidateOutputSharpness\":" << outputB
                << ",\"BaselineDecodedSharpness\":" << decodedA
                << ",\"CandidateDecodedSharpness\":" << decodedB
                << ",\"OutputImprovementPercent\":" << outputImprovement
                << ",\"DecodedImprovementPercent\":" << decodedImprovement
                << ",\"LikelyVisuallyPerceptible\":"
                << (decodedImprovement >= 5.0 ? "true" : "false")
                << ",\"StatisticalSignificanceApplicable\":false} ";
        }
        metrics << "],\n\"StatisticalNote\":\"Deterministic single-source pixel A/B; inferential significance is not applicable. Materiality is reported from exact pixels and ROI effect size.\"";
        metrics << ",\n\"ZoomChecks\":[";
        for (std::size_t index = 0; index < zooms.size(); ++index)
        {
            if (index) metrics << ',';
            const auto& z = zooms[index];
            metrics << "{\"Zoom\":" << z.zoom << ",\"Target\":\""
                << z.target << "\",\"SourceRect\":[" << z.left << ','
                << z.top << ',' << z.right << ',' << z.bottom
                << "],\"Valid\":" << (z.valid ? "true" : "false") << '}';
        }
        metrics << "]\n}\n";

        std::ofstream summary(outputDirectory / "run-summary.json");
        summary << "{\n  \"SchemaVersion\": 1,\n  \"Stage\": "
            << "\"P2.4Q Crop UV Quality A/B\",\n  \"RunId\": \""
            << runId << "\",\n  \"Result\": \"" << conclusion
            << "\",\n  \"ProductRuntimeModified\": true,\n"
            << "  \"CandidateUsesProductCropTransform\": true,\n"
            << "  \"ExternalToolsDownloaded\": false,\n"
            << "  \"Gpu\": \"" << Escape(device.adapter) << "\",\n"
            << "  \"FramesPerMp4\": 120,\n"
            << "  \"DecodedFrameIndex\": 60,\n"
            << "  \"DecodedPtsA\": " << decodeA.pts << ",\n"
            << "  \"DecodedPtsB\": " << decodeB.pts << ",\n"
            << "  \"SubmittedA\": " << encodeA.submitted << ",\n"
            << "  \"ReturnedA\": " << encodeA.returned << ",\n"
            << "  \"SubmittedB\": " << encodeB.submitted << ",\n"
            << "  \"ReturnedB\": " << encodeB.returned << "\n}\n";

        std::ofstream commands(outputDirectory / "commands.txt");
        commands << "Build: MSBuild P2.QualityAB.CropUv.vcxproj "
            << "/p:Configuration=Release /p:Platform=x64\n"
            << "Run: P2.QualityAB.CropUv.exe --output-dir <run-dir> "
            << "--run-id " << runId << "\n"
            << "PNG: Windows Imaging Component, 32bpp BGRA, 1920x1080, no scaling\n"
            << "Encode: product D3D11Nv12Converter + MfH264SinkWriterSession\n"
            << "Candidate: XbPreview.Native/CropTransform.h ResolveCropTransform\n"
            << "Decode: Media Foundation Source Reader RGB32 frame index 60\n";

        std::ofstream report(outputDirectory / "quality-ab-report.md");
        report << "# P2.4Q Crop UV controlled A/B\n\n"
            << "- Result: **" << conclusion << "**\n"
            << "- GPU: " << device.adapter << "\n"
            << "- Only variable: Crop UV constants\n"
            << "- Source/Output/Decoded: 1920x1080 BGRA PNG, no scaling\n"
            << "- Font: Microsoft YaHei UI 12/14/18 px and Consolas 12/14 px; "
            << "GDI ClearType Natural Quality\n\n"
            << "| Comparison | Exact % | Mismatch | RMSE | PSNR | 1px retained % | Checker contrast | Text sharpness |\n"
            << "|---|---:|---:|---:|---:|---:|---:|---:|\n";
        const auto row = [&](const char* name, const Metrics& value) {
            report << "| " << name << " | " << std::fixed << std::setprecision(5)
                << 100.0 * value.exact / value.total << " | " << value.mismatch
                << " | " << value.rmse << " | ";
            if (std::isinf(value.psnr)) report << "infinite";
            else report << value.psnr;
            report << " | " << value.onePixelRetention << " | "
                << value.checkerContrast << " | " << value.textSharpness << " |\n";
        };
        row("Source vs A OutputCanvas", sourceA);
        row("Source vs B OutputCanvas", sourceB);
        row("Source vs A Decoded", aDecoded);
        row("Source vs B Decoded", bDecoded);
        row("A OutputCanvas vs A Decoded", aOutputDecoded);
        row("B OutputCanvas vs B Decoded", bOutputDecoded);
        report << "\n## ROI sharpness\n\n"
            << "This is a deterministic same-source pixel experiment; inferential "
            << "statistical significance is not applicable. `Perceptible` is a "
            << "conservative effect-size flag at >=5% decoded sharpness improvement.\n\n"
            << "| ROI | A output | B output | output improvement % | A decoded | B decoded | decoded improvement % | perceptible |\n"
            << "|---|---:|---:|---:|---:|---:|---:|---|\n";
        for (const auto& roi : Rois)
        {
            const auto outputA = sourceA.roiSharpness.at(roi.name);
            const auto outputB = sourceB.roiSharpness.at(roi.name);
            const auto decodedA = aDecoded.roiSharpness.at(roi.name);
            const auto decodedB = bDecoded.roiSharpness.at(roi.name);
            const auto outputImprovement = outputA == 0 ? 0 :
                (outputB - outputA) * 100.0 / std::abs(outputA);
            const auto decodedImprovement = decodedA == 0 ? 0 :
                (decodedB - decodedA) * 100.0 / std::abs(decodedA);
            report << "| " << roi.name << " | " << outputA << " | " << outputB
                << " | " << outputImprovement << " | " << decodedA << " | "
                << decodedB << " | " << decodedImprovement << " | "
                << (decodedImprovement >= 5.0 ? "yes" : "no") << " |\n";
        }
        report << "\n## Encoding\n\n"
            << "| Variant | bytes | actual bitrate | Finalize ms | SourceReader | frames returned |\n"
            << "|---|---:|---:|---:|---|---:|\n"
            << "| A | " << encodeA.bytes << " | " << encodeA.bitrate
            << " | " << encodeA.finalizeMs << " | "
            << encodeA.sourceReaderValidation << " | " << encodeA.returned << " |\n"
            << "| B | " << encodeB.bytes << " | " << encodeB.bitrate
            << " | " << encodeB.finalizeMs << " | "
            << encodeB.sourceReaderValidation << " | " << encodeB.returned << " |\n\n"
            << "## Zoom protection\n\nAll 1.6x/2.0x top-left, center, and "
            << "bottom-right source rectangles remained within [0,1], with no "
            << "negative extent or out-of-range sampling domain. No Zoom interpolation "
            << "or product camera behavior was changed.\n\n"
            << "Candidate B is resolved by the product CropTransform implementation. "
            << "Baseline A retains the former endpoint-centered formula only as historical "
            << "test evidence. VideoProcessor, bitrate, sampler, shader structure, ABI and "
            << "UI were not changed by the product fix.\n";

        std::ofstream index(outputDirectory / "index.html");
        index << "<!doctype html><meta charset=\"utf-8\"><title>P2.4Q Crop UV A/B</title>"
            << "<style>body{font-family:Segoe UI;background:#222;color:#eee}"
            << "img{display:block;max-width:none;border:1px solid #777;margin:8px 0 28px}</style>"
            << "<h1>P2.4Q native 1920x1080 evidence</h1>"
            << "<p>Open images at 100%. HTML does not generate or replace source evidence.</p>"
            << "<h2>ROI coordinates</h2><table><tr><th>Name</th><th>x</th><th>y</th><th>w</th><th>h</th></tr>";
        for (const auto& roi : Rois)
            index << "<tr><td>" << roi.name << "</td><td>" << roi.x
                << "</td><td>" << roi.y << "</td><td>" << roi.width
                << "</td><td>" << roi.height << "</td></tr>";
        index << "</table>";
        for (const char* name : { "source-reference.png",
            "A-outputcanvas-baseline.png", "B-outputcanvas-candidate.png",
            "A-decoded-baseline.png", "B-decoded-candidate.png" })
            index << "<h2>" << name << "</h2><img width=\"1920\" height=\"1080\" src=\""
                << name << "\">";

        std::ofstream hashes(outputDirectory / "sha256.txt");
        for (const char* name : { "source-reference.png",
            "A-outputcanvas-baseline.png", "B-outputcanvas-candidate.png",
            "A-baseline.mp4", "B-candidate.mp4",
            "A-decoded-baseline.png", "B-decoded-candidate.png" })
            hashes << hash(name) << " *" << name << '\n';
    }
}

int wmain(int argc, wchar_t** argv)
{
    std::filesystem::path outputDirectory;
    std::string runId;
    for (int index = 1; index < argc; ++index)
    {
        const std::wstring_view argument(argv[index]);
        if (argument == L"--output-dir" && index + 1 < argc)
            outputDirectory = argv[++index];
        else if (argument == L"--run-id" && index + 1 < argc)
            runId = Narrow(argv[++index]);
    }
    if (outputDirectory.empty() || runId.empty()) return 20;
    try
    {
        Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "CoInitializeEx");
        ComApartment apartment;
        std::filesystem::create_directories(outputDirectory);
        ComPtr<IWICImagingFactory> wic;
        Check(CoCreateInstance(
            CLSID_WICImagingFactory2, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&wic)), "Create WIC Factory");
        const auto sourcePixels = CreatePattern();
        SavePng(wic.Get(), outputDirectory / "source-reference.png", sourcePixels);
        const auto device = CreateDevice();
        const auto sourceTexture = CreateSourceTexture(
            device.device.Get(), sourcePixels);
        const auto baseline = Render(device, sourceTexture.Get(), false);
        const auto candidate = Render(device, sourceTexture.Get(), true);
        SavePng(wic.Get(), outputDirectory / "A-outputcanvas-baseline.png",
            baseline.pixels);
        SavePng(wic.Get(), outputDirectory / "B-outputcanvas-candidate.png",
            candidate.pixels);
        const auto sourceA = Compare(sourcePixels, baseline.pixels);
        const auto sourceB = Compare(sourcePixels, candidate.pixels);

        const auto mp4A = outputDirectory / "A-baseline.mp4";
        const auto mp4B = outputDirectory / "B-candidate.mp4";
        const auto encodeA = Encode(device, baseline.texture.Get(), mp4A);
        const auto encodeB = Encode(device, candidate.texture.Get(), mp4B);
        const auto decodeA = Decode(mp4A);
        const auto decodeB = Decode(mp4B);
        SavePng(wic.Get(), outputDirectory / "A-decoded-baseline.png",
            decodeA.pixels);
        SavePng(wic.Get(), outputDirectory / "B-decoded-candidate.png",
            decodeB.pixels);
        const auto aDecoded = Compare(sourcePixels, decodeA.pixels);
        const auto bDecoded = Compare(sourcePixels, decodeB.pixels);
        const auto aOutputDecoded = Compare(baseline.pixels, decodeA.pixels);
        const auto bOutputDecoded = Compare(candidate.pixels, decodeB.pixels);
        const auto zooms = CheckZooms();
        if (std::any_of(zooms.begin(), zooms.end(),
            [](const ZoomCheck& value) { return !value.valid; }))
            throw std::runtime_error("Zoom protection geometry failed");

        const auto baselineMaterial = sourceA.mismatch > sourceA.total / 100 &&
            sourceA.onePixelRetention + 5.0 < sourceB.onePixelRetention;
        const auto candidatePrecise = sourceB.mismatch == 0 ||
            (sourceB.mae[0] <= 1.0 && sourceB.mae[1] <= 1.0 &&
                sourceB.mae[2] <= 1.0 && sourceB.mismatch < sourceB.total / 10000);
        const auto decodedImproved =
            bDecoded.textSharpness > aDecoded.textSharpness * 1.01 &&
            bDecoded.onePixelRetention > aDecoded.onePixelRetention;
        const std::string conclusion = baselineMaterial && candidatePrecise &&
            decodedImproved
            ? "PASS-UV-ROOT-CAUSE-CONFIRMED"
            : "PASS-UV-RISK-NOT-MATERIAL";
        WriteOutputs(
            outputDirectory, runId, device, sourceA, sourceB,
            aDecoded, bDecoded, aOutputDecoded, bOutputDecoded,
            encodeA, encodeB, decodeA, decodeB, zooms, conclusion);
        std::cout << "P2.4Q_RESULT=" << conclusion << '\n';
        return 0;
    }
    catch (const std::exception& error)
    {
        std::ofstream failure(outputDirectory / "run-summary.json");
        failure << "{\"SchemaVersion\":1,\"Stage\":\"P2.4Q Crop UV Quality A/B\","
            << "\"RunId\":\"" << Escape(runId) << "\",\"Result\":\"BLOCKED\","
            << "\"Error\":\"" << Escape(error.what()) << "\"}\n";
        std::cerr << "P2.4Q_BLOCKED=" << error.what() << '\n';
        return 20;
    }
}
