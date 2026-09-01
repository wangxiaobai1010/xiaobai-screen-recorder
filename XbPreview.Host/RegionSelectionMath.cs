namespace XbPreview.Host;

using System.Globalization;

internal enum RegionAspectMode
{
    Free,
    Ratio16By9,
}

internal enum RegionResizeHandle
{
    None,
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

internal readonly record struct PhysicalPixelPoint(int X, int Y);

internal enum ExactSizeEditedDimension
{
    Width,
    Height,
}

internal static class RegionSelectionMath
{
    internal const int DragActivationThresholdPixels = 4;

    internal static bool TryCreateFromDrag(
        PhysicalPixelPoint anchor,
        PhysicalPixelPoint pointer,
        int sourceWidth,
        int sourceHeight,
        RegionAspectMode aspectMode,
        out CaptureRegion region)
    {
        ValidateSource(sourceWidth, sourceHeight);
        PhysicalPixelPoint safeAnchor = ClampPoint(anchor, sourceWidth, sourceHeight);
        PhysicalPixelPoint safePointer = ClampPoint(pointer, sourceWidth, sourceHeight);
        int rawWidth = Math.Abs(safePointer.X - safeAnchor.X);
        int rawHeight = Math.Abs(safePointer.Y - safeAnchor.Y);
        if (rawWidth < DragActivationThresholdPixels ||
            rawHeight < DragActivationThresholdPixels)
        {
            region = default;
            return false;
        }

        int width = rawWidth;
        int height = rawHeight;
        if (aspectMode == RegionAspectMode.Ratio16By9)
        {
            (width, height) = Fit16By9Dimensions(rawWidth, rawHeight);
        }

        int left = safePointer.X >= safeAnchor.X
            ? safeAnchor.X
            : safeAnchor.X - width;
        int top = safePointer.Y >= safeAnchor.Y
            ? safeAnchor.Y
            : safeAnchor.Y - height;
        region = CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
        return true;
    }

    internal static CaptureRegion Move(
        CaptureRegion region,
        int deltaX,
        int deltaY,
        int sourceWidth,
        int sourceHeight)
    {
        EnsureWithin(region, sourceWidth, sourceHeight);
        int left = Clamp(
            checked(region.Left + deltaX),
            0,
            sourceWidth - region.Width);
        int top = Clamp(
            checked(region.Top + deltaY),
            0,
            sourceHeight - region.Height);
        return CaptureRegion.Create(
            left,
            top,
            region.Width,
            region.Height,
            sourceWidth,
            sourceHeight);
    }

    internal static CaptureRegion Resize(
        CaptureRegion original,
        RegionResizeHandle handle,
        PhysicalPixelPoint pointer,
        int sourceWidth,
        int sourceHeight,
        RegionAspectMode aspectMode)
    {
        EnsureWithin(original, sourceWidth, sourceHeight);
        PhysicalPixelPoint safe = ClampPoint(pointer, sourceWidth, sourceHeight);
        if (handle is RegionResizeHandle.None or RegionResizeHandle.Move)
        {
            return original;
        }

        return aspectMode == RegionAspectMode.Free
            ? ResizeFree(original, handle, safe, sourceWidth, sourceHeight)
            : Resize16By9(original, handle, safe, sourceWidth, sourceHeight);
    }

    internal static CaptureRegion FitLargest16By9Inside(
        CaptureRegion region,
        int sourceWidth,
        int sourceHeight)
    {
        EnsureWithin(region, sourceWidth, sourceHeight);
        (int width, int height) =
            Fit16By9Dimensions(region.Width, region.Height);
        int left = region.Left + ((region.Width - width) / 2);
        int top = region.Top + ((region.Height - height) / 2);
        return CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
    }

    internal static RegionResizeHandle HitTest(
        CaptureRegion region,
        PhysicalPixelPoint point,
        int handleRadiusPixels)
    {
        if (handleRadiusPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handleRadiusPixels));
        }

        bool nearLeft = Math.Abs(point.X - region.Left) <= handleRadiusPixels;
        bool nearRight = Math.Abs(point.X - region.Right) <= handleRadiusPixels;
        bool nearTop = Math.Abs(point.Y - region.Top) <= handleRadiusPixels;
        bool nearBottom = Math.Abs(point.Y - region.Bottom) <= handleRadiusPixels;
        if (nearLeft && nearTop) return RegionResizeHandle.TopLeft;
        if (nearRight && nearTop) return RegionResizeHandle.TopRight;
        if (nearLeft && nearBottom) return RegionResizeHandle.BottomLeft;
        if (nearRight && nearBottom) return RegionResizeHandle.BottomRight;
        if (nearLeft && point.Y >= region.Top && point.Y <= region.Bottom)
            return RegionResizeHandle.Left;
        if (nearRight && point.Y >= region.Top && point.Y <= region.Bottom)
            return RegionResizeHandle.Right;
        if (nearTop && point.X >= region.Left && point.X <= region.Right)
            return RegionResizeHandle.Top;
        if (nearBottom && point.X >= region.Left && point.X <= region.Right)
            return RegionResizeHandle.Bottom;
        return region.Contains(point.X, point.Y)
            ? RegionResizeHandle.Move
            : RegionResizeHandle.None;
    }

    internal static (int Width, int Height) Fit16By9Dimensions(
        int rawWidth,
        int rawHeight)
    {
        if (rawWidth <= 0 || rawHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawWidth));
        }

        long widthTimesNine = (long)rawWidth * 9;
        long heightTimesSixteen = (long)rawHeight * 16;
        if (widthTimesNine > heightTimesSixteen)
        {
            int width = checked((int)((long)rawHeight * 16 / 9));
            return (Math.Max(1, width), rawHeight);
        }
        if (widthTimesNine < heightTimesSixteen)
        {
            int height = checked((int)((long)rawWidth * 9 / 16));
            return (rawWidth, Math.Max(1, height));
        }
        return (rawWidth, rawHeight);
    }

    internal static bool TryResolveExactSize(
        string widthText,
        string heightText,
        RegionAspectMode aspectMode,
        ExactSizeEditedDimension lastEditedDimension,
        int sourceWidth,
        int sourceHeight,
        out int width,
        out int height,
        out string? error)
    {
        ValidateSource(sourceWidth, sourceHeight);
        width = 0;
        height = 0;
        error = null;

        if (aspectMode == RegionAspectMode.Free)
        {
            if (!TryParsePositiveDimension(widthText, out width) ||
                !TryParsePositiveDimension(heightText, out height))
            {
                error = "宽度和高度必须是正整数。";
                return false;
            }
        }
        else if (lastEditedDimension == ExactSizeEditedDimension.Width)
        {
            if (!TryParsePositiveDimension(widthText, out width))
            {
                error = "宽度必须是正整数。";
                return false;
            }
            if (!TryCalculateLinkedDimension(
                widthText,
                ExactSizeEditedDimension.Width,
                out height))
            {
                error = "宽度超出可处理范围。";
                width = 0;
                return false;
            }
        }
        else
        {
            if (!TryParsePositiveDimension(heightText, out height))
            {
                error = "高度必须是正整数。";
                return false;
            }
            if (!TryCalculateLinkedDimension(
                heightText,
                ExactSizeEditedDimension.Height,
                out width))
            {
                error = "高度超出可处理范围。";
                height = 0;
                return false;
            }
        }

        if (width > sourceWidth || height > sourceHeight)
        {
            error =
                $"输入尺寸超出当前显示器。最大可用尺寸：{sourceWidth} × {sourceHeight}。";
            width = 0;
            height = 0;
            return false;
        }
        return true;
    }

    internal static bool TryCalculateLinkedDimension(
        string editedText,
        ExactSizeEditedDimension editedDimension,
        out int linkedDimension)
    {
        linkedDimension = 0;
        if (!TryParsePositiveDimension(editedText, out int edited))
        {
            return false;
        }
        try
        {
            linkedDimension = editedDimension == ExactSizeEditedDimension.Width
                ? Math.Max(1, checked((int)((long)edited * 9 / 16)))
                : Math.Max(1, checked((int)((long)edited * 16 / 9)));
            return true;
        }
        catch (OverflowException)
        {
            linkedDimension = 0;
            return false;
        }
    }

    internal static CaptureRegion ApplyExactSize(
        CaptureRegion original,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight)
    {
        EnsureWithin(original, sourceWidth, sourceHeight);
        if (width <= 0 ||
            height <= 0 ||
            width > sourceWidth ||
            height > sourceHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Exact dimensions must fit inside the source.");
        }

        long doubledCenterX = ((long)original.Left * 2) + original.Width;
        long doubledCenterY = ((long)original.Top * 2) + original.Height;
        int left = checked((int)((doubledCenterX - width) / 2));
        int top = checked((int)((doubledCenterY - height) / 2));
        left = Clamp(left, 0, sourceWidth - width);
        top = Clamp(top, 0, sourceHeight - height);
        return CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
    }

    private static bool TryParsePositiveDimension(
        string text,
        out int value) =>
        int.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value) &&
        value > 0;

    private static CaptureRegion ResizeFree(
        CaptureRegion original,
        RegionResizeHandle handle,
        PhysicalPixelPoint pointer,
        int sourceWidth,
        int sourceHeight)
    {
        int left = original.Left;
        int top = original.Top;
        int right = original.Right;
        int bottom = original.Bottom;
        if (MovesLeft(handle))
        {
            left = Clamp(pointer.X, 0, right - 1);
        }
        if (MovesRight(handle))
        {
            right = Clamp(pointer.X, left + 1, sourceWidth);
        }
        if (MovesTop(handle))
        {
            top = Clamp(pointer.Y, 0, bottom - 1);
        }
        if (MovesBottom(handle))
        {
            bottom = Clamp(pointer.Y, top + 1, sourceHeight);
        }

        return CaptureRegion.Create(
            left,
            top,
            right - left,
            bottom - top,
            sourceWidth,
            sourceHeight);
    }

    private static CaptureRegion Resize16By9(
        CaptureRegion original,
        RegionResizeHandle handle,
        PhysicalPixelPoint pointer,
        int sourceWidth,
        int sourceHeight)
    {
        if (handle is RegionResizeHandle.Left or RegionResizeHandle.Right)
        {
            return ResizeHorizontalEdge16By9(
                original,
                handle,
                pointer.X,
                sourceWidth,
                sourceHeight);
        }
        if (handle is RegionResizeHandle.Top or RegionResizeHandle.Bottom)
        {
            return ResizeVerticalEdge16By9(
                original,
                handle,
                pointer.Y,
                sourceWidth,
                sourceHeight);
        }

        int fixedX = MovesLeft(handle) ? original.Right : original.Left;
        int fixedY = MovesTop(handle) ? original.Bottom : original.Top;
        int movingX = MovesLeft(handle)
            ? Clamp(pointer.X, 0, fixedX - 1)
            : Clamp(pointer.X, fixedX + 1, sourceWidth);
        int movingY = MovesTop(handle)
            ? Clamp(pointer.Y, 0, fixedY - 1)
            : Clamp(pointer.Y, fixedY + 1, sourceHeight);
        int rawWidth = Math.Abs(movingX - fixedX);
        int rawHeight = Math.Abs(movingY - fixedY);
        rawWidth = Math.Max(1, rawWidth);
        rawHeight = Math.Max(1, rawHeight);
        (int width, int height) = Fit16By9Dimensions(rawWidth, rawHeight);

        int maximumWidth = MovesLeft(handle) ? fixedX : sourceWidth - fixedX;
        int maximumHeight = MovesTop(handle) ? fixedY : sourceHeight - fixedY;
        (width, height) = Constrain16By9(
            width,
            height,
            maximumWidth,
            maximumHeight);
        int left = MovesLeft(handle) ? fixedX - width : fixedX;
        int top = MovesTop(handle) ? fixedY - height : fixedY;
        return CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
    }

    private static CaptureRegion ResizeHorizontalEdge16By9(
        CaptureRegion original,
        RegionResizeHandle handle,
        int pointerX,
        int sourceWidth,
        int sourceHeight)
    {
        bool moveLeft = handle == RegionResizeHandle.Left;
        int fixedX = moveLeft ? original.Right : original.Left;
        int maximumWidth = moveLeft ? fixedX : sourceWidth - fixedX;
        int movingX = moveLeft
            ? Clamp(pointerX, 0, fixedX - 1)
            : Clamp(pointerX, fixedX + 1, sourceWidth);
        int requestedWidth = Clamp(
            Math.Abs(movingX - fixedX),
            1,
            maximumWidth);
        int requestedHeight = Math.Max(
            1,
            checked((int)((long)requestedWidth * 9 / 16)));
        (int width, int height) = Constrain16By9(
            requestedWidth,
            requestedHeight,
            maximumWidth,
            sourceHeight);

        int doubledCenterY = checked((original.Top * 2) + original.Height);
        int top = Clamp(
            (doubledCenterY - height) / 2,
            0,
            sourceHeight - height);
        int left = moveLeft ? fixedX - width : fixedX;
        return CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
    }

    private static CaptureRegion ResizeVerticalEdge16By9(
        CaptureRegion original,
        RegionResizeHandle handle,
        int pointerY,
        int sourceWidth,
        int sourceHeight)
    {
        bool moveTop = handle == RegionResizeHandle.Top;
        int fixedY = moveTop ? original.Bottom : original.Top;
        int maximumHeight = moveTop ? fixedY : sourceHeight - fixedY;
        int movingY = moveTop
            ? Clamp(pointerY, 0, fixedY - 1)
            : Clamp(pointerY, fixedY + 1, sourceHeight);
        int requestedHeight = Clamp(
            Math.Abs(movingY - fixedY),
            1,
            maximumHeight);
        int requestedWidth = Math.Max(
            1,
            checked((int)((long)requestedHeight * 16 / 9)));
        (int width, int height) = Constrain16By9(
            requestedWidth,
            requestedHeight,
            sourceWidth,
            maximumHeight);

        int doubledCenterX = checked((original.Left * 2) + original.Width);
        int left = Clamp(
            (doubledCenterX - width) / 2,
            0,
            sourceWidth - width);
        int top = moveTop ? fixedY - height : fixedY;
        return CaptureRegion.Create(
            left,
            top,
            width,
            height,
            sourceWidth,
            sourceHeight);
    }

    private static (int Width, int Height) Constrain16By9(
        int width,
        int height,
        int maximumWidth,
        int maximumHeight)
    {
        width = Clamp(width, 1, Math.Max(1, maximumWidth));
        height = Clamp(height, 1, Math.Max(1, maximumHeight));
        (width, height) = Fit16By9Dimensions(width, height);
        if (width > maximumWidth)
        {
            width = maximumWidth;
            height = Math.Max(1, checked((int)((long)width * 9 / 16)));
        }
        if (height > maximumHeight)
        {
            height = maximumHeight;
            width = Math.Max(1, checked((int)((long)height * 16 / 9)));
        }
        return (width, height);
    }

    private static PhysicalPixelPoint ClampPoint(
        PhysicalPixelPoint point,
        int sourceWidth,
        int sourceHeight) =>
        new(
            Clamp(point.X, 0, sourceWidth),
            Clamp(point.Y, 0, sourceHeight));

    private static bool MovesLeft(RegionResizeHandle handle) =>
        handle is RegionResizeHandle.Left or
            RegionResizeHandle.TopLeft or
            RegionResizeHandle.BottomLeft;

    private static bool MovesRight(RegionResizeHandle handle) =>
        handle is RegionResizeHandle.Right or
            RegionResizeHandle.TopRight or
            RegionResizeHandle.BottomRight;

    private static bool MovesTop(RegionResizeHandle handle) =>
        handle is RegionResizeHandle.Top or
            RegionResizeHandle.TopLeft or
            RegionResizeHandle.TopRight;

    private static bool MovesBottom(RegionResizeHandle handle) =>
        handle is RegionResizeHandle.Bottom or
            RegionResizeHandle.BottomLeft or
            RegionResizeHandle.BottomRight;

    private static void EnsureWithin(
        CaptureRegion region,
        int sourceWidth,
        int sourceHeight)
    {
        ValidateSource(sourceWidth, sourceHeight);
        if (!region.IsWithin(sourceWidth, sourceHeight))
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }
    }

    private static void ValidateSource(int sourceWidth, int sourceHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }
    }

    private static int Clamp(int value, int minimum, int maximum) =>
        Math.Min(maximum, Math.Max(minimum, value));
}
