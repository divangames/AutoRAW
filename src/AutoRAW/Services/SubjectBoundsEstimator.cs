using AutoRAW.Models;
using OpenCvSharp;

namespace AutoRAW.Services;

/// <summary>
/// Ищет bbox основного объекта на предметной съёмке.
/// Стратегии (в порядке приоритета):
///   1. flood-fill от краёв → изолируем «висящие» объекты, не связные с фоном
///   2. relaxed — убираем только тонкие горизонтальные полосы снизу (стол/пол)
///   3. fallback — центральный прямоугольник
/// Предварительно отсекаем тёмную горизонтальную полосу снизу (стол/плинтус в кадре).
/// </summary>
public static class SubjectBoundsEstimator
{
    /// <summary>Баланс скорость/RAW: ниже — быстрее пакет и превью; ползунок на главном окне можно поднять.</summary>
    public const int DefaultAnalysisMaxEdge = 1024;

    /// <summary>
    /// Сужает ширину bbox по вертикальной проекции краёв (Sobel) внутри прямоугольника.
    /// Убирает «лишнюю» ширину от тени/фона, из‑за которой зум по line-guide получается слабым.
    /// </summary>
    public static Box2d RefineHorizontalWidthByEdgeProjection(Mat bgr, Box2d subject)
    {
        if (bgr.Empty() || subject.Width < 16 || subject.Height < 16)
            return subject;

        using var bgr3 = EnsureBgr(bgr);

        var x0 = (int)Math.Floor(Math.Max(0, subject.X));
        var y0 = (int)Math.Floor(Math.Max(0, subject.Y));
        var x1 = (int)Math.Ceiling(Math.Min(bgr.Cols, subject.Right));
        var y1 = (int)Math.Ceiling(Math.Min(bgr.Rows, subject.Bottom));
        if (x1 - x0 < 16 || y1 - y0 < 12)
            return subject;

        using var roi = new Mat(bgr3, new OpenCvSharp.Rect(x0, y0, x1 - x0, y1 - y0));
        using var gray = new Mat();
        Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);

        using var gx = new Mat();
        using var gy = new Mat();
        Cv2.Sobel(gray, gx, MatType.CV_16S, 1, 0, ksize: 3);
        Cv2.Sobel(gray, gy, MatType.CV_16S, 0, 1, ksize: 3);
        using var ax = new Mat();
        using var ay = new Mat();
        Cv2.ConvertScaleAbs(gx, ax);
        Cv2.ConvertScaleAbs(gy, ay);
        using var mag = new Mat();
        Cv2.Max(ax, ay, mag);

        var colSum = new int[mag.Cols];
        var maxSum = 0;
        for (var xc = 0; xc < mag.Cols; xc++)
        {
            var s = 0;
            for (var yr = 0; yr < mag.Rows; yr++)
                s += mag.At<byte>(yr, xc);
            colSum[xc] = s;
            if (s > maxSum)
                maxSum = s;
        }

        if (maxSum < 80)
            return subject;

        var thresh = Math.Max(12, maxSum / 14);
        var left = -1;
        var right = -1;
        for (var i = 0; i < colSum.Length; i++)
        {
            if (colSum[i] < thresh)
                continue;
            left = i;
            break;
        }

        for (var i = colSum.Length - 1; i >= 0; i--)
        {
            if (colSum[i] < thresh)
                continue;
            right = i;
            break;
        }

        if (left < 0 || right <= left)
            return subject;

        var tightPx = right - left + 1;
        if (tightPx < subject.Width * 0.58)
            return subject;

        var tightW = (double)tightPx;
        var cx = subject.CenterX;
        var nx = cx - tightW * 0.5;
        nx = Math.Clamp(nx, 0, Math.Max(0, bgr.Cols - tightW));
        return new Box2d(nx, subject.Y, tightW, subject.Height);
    }

    /// <summary>
    /// Оценка товара только для <c>zona/…/operation/NN</c> (макеты 01_center / 02_crop).
    /// Упрощённая цепочка как до доработок под line-guide и белый фон — стабильный зум для кадров 02,03,04,06,08 и др.
    /// Обычный <see cref="Estimate"/> по-прежнему для reference, line-guide и обычного Zona.
    /// </summary>
    public static Box2d EstimateForOperation(Mat bgr)
    {
        if (bgr.Empty())
            return new Box2d(0, 0, 1, 1);

        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new OpenCvSharp.Size(7, 7), 0);

        var cols = bgr.Cols;
        var rows = bgr.Rows;
        return TryFloodFillIsolation(blur, cols, rows)
               ?? TryRelaxedComponentFilterOperationLegacy(blur, cols, rows)
               ?? Box2d.CenterFraction(cols, rows, 0.68, 0.68);
    }

    public static Box2d Estimate(Mat bgr)
    {
        if (bgr.Empty())
            return new Box2d(0, 0, 1, 1);

        using var bgrWork = EnsureBgr(bgr);
        using var gray = new Mat();
        Cv2.CvtColor(bgrWork, gray, ColorConversionCodes.BGR2GRAY);
        using var blur = new Mat();
        Cv2.GaussianBlur(gray, blur, new OpenCvSharp.Size(7, 7), 0);

        var cols = bgrWork.Cols;
        var rows = bgrWork.Rows;

        // Отсекаем тёмную горизонтальную полосу снизу (стол/плинтус в предметной съёмке)
        var effectiveRows = FindEffectiveRows(blur, cols, rows);

        using var analysisBlur = effectiveRows < rows
            ? new Mat(blur, new OpenCvSharp.Rect(0, 0, cols, effectiveRows))
            : blur.Clone();

        // Стратегии на основе яркости (OTSU): работают хорошо для контрастных объектов (тёмная обувь)
        var intensityBox = TryFloodFillIsolation(analysisBlur, cols, effectiveRows)
                        ?? TryRelaxedComponentFilter(analysisBlur, cols, effectiveRows);

        // Если интенсивностная стратегия нашла слишком маленький объект (белая обувь на белом фоне),
        // используем градиентный метод, который работает по краям независимо от яркости.
        const double goodBoxAreaFrac = 0.05;
        var box = intensityBox != null
                  && intensityBox.Value.Width * intensityBox.Value.Height
                     >= (double)cols * effectiveRows * goodBoxAreaFrac
            ? intensityBox.Value
            : TryGradientObjectDetection(analysisBlur, cols, effectiveRows)
              ?? Box2d.CenterFraction(cols, effectiveRows, 0.68, 0.68);

        return ExpandIfSubjectTooSmall(box, cols, effectiveRows);
    }

    /// <summary>
    /// Определяет эффективную высоту кадра, отсекая тёмную горизонтальную полосу снизу.
    /// Стол/плинтус в предметной съёмке значительно темнее фона и всегда примыкает к нижнему краю.
    /// </summary>
    private static int FindEffectiveRows(Mat blur, int cols, int rows)
    {
        // Ориентируемся по верхним 65% кадра как «фон + товар»
        var topRefEnd = (int)(rows * 0.65);
        if (topRefEnd < 1)
            return rows;

        using var topRegion = new Mat(blur, new OpenCvSharp.Rect(0, 0, cols, topRefEnd));
        var topMean = Cv2.Mean(topRegion).Val0;

        // Очень тёмное изображение целиком — не пытаемся вырезать полосу
        if (topMean < 40)
            return rows;

        // Строка с яркостью ниже 60% от среднего фона считается полосой стола/плинтуса
        var darkThreshold = topMean * 0.60;

        // Ищем верхний край тёмной полосы, идя снизу вверх (только в нижних 35%)
        var cutRow = rows;
        for (var y = rows - 1; y >= topRefEnd; y--)
        {
            using var rowView = new Mat(blur, new OpenCvSharp.Rect(0, y, cols, 1));
            var rowMean = Cv2.Mean(rowView).Val0;
            if (rowMean < darkThreshold)
                cutRow = y;
            else
                break;
        }

        // Защита: не обрезаем более 40% кадра
        return Math.Max(cutRow, (int)(rows * 0.60));
    }

    /// <summary>Если детектор захватил только фрагмент (каблук, язычок) — расширяем bbox.</summary>
    private static Box2d ExpandIfSubjectTooSmall(Box2d box, int cols, int effectiveRows)
    {
        var areaFrac = box.Width * box.Height / Math.Max(1.0, (double)cols * effectiveRows);
        if (areaFrac >= 0.14)
            return box;

        var targetW = cols * 0.58;
        var targetH = effectiveRows * 0.48;
        var cx = box.CenterX;
        var cy = box.CenterY;
        var x = cx - targetW / 2;
        var y = cy - targetH / 2;
        x = Math.Clamp(x, 0, Math.Max(0, cols - targetW));
        y = Math.Clamp(y, 0, Math.Max(0, effectiveRows - targetH));
        return new Box2d(x, y, targetW, targetH);
    }

    // ----------------------------------------------------------------
    // Стратегия 1: flood-fill от краёв кадра — фон «стекает», объект остаётся
    // ----------------------------------------------------------------
    private static Box2d? TryFloodFillIsolation(Mat blur, int cols, int rows)
    {
        Box2d? best = null;
        double bestArea = 0;

        foreach (var invert in new[] { false, true })
        {
            using var bin = new Mat();
            Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            if (invert)
                Cv2.BitwiseNot(bin, bin);

            // Заливаем из всех пикселей по периметру → помечаем «фон»
            using var filled = bin.Clone();
            FillFromBorder(filled, cols, rows);

            // subject = пиксели, которые были 255 до и не были залиты (остались 255)
            using var subject = new Mat();
            Cv2.InRange(filled, new Scalar(255), new Scalar(255), subject);

            // Морфология: закрываем мелкие дыры
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(9, 9));
            using var closed = new Mat();
            Cv2.MorphologyEx(subject, closed, MorphTypes.Close, kernel);

            var box = LargestConnectedBbox(closed, cols, rows, minFrac: 0.004, maxFrac: 0.92);
            if (box is { } b && b.Width * b.Height > bestArea)
            {
                bestArea = b.Width * b.Height;
                best = b;
            }
        }

        return best;
    }

    /// <summary>Расслабленный фильтр как в старой версии (до ослабления порогов для операции).</summary>
    private static Box2d? TryRelaxedComponentFilterOperationLegacy(Mat blur, int cols, int rows)
    {
        Box2d? best = null;
        double bestArea = 0;
        double totalArea = (double)cols * rows;

        foreach (var invert in new[] { false, true })
        {
            using var bin = new Mat();
            Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            if (invert)
                Cv2.BitwiseNot(bin, bin);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
            using var closed = new Mat();
            Cv2.MorphologyEx(bin, closed, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(closed, labels, stats, centroids);

            for (int i = 1; i < n; i++)
            {
                int bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

                if (area < totalArea * 0.003 || area > totalArea * 0.92)
                    continue;

                bool bottomStrip = (by + bh >= rows - 2)
                                && bh < rows * 0.12
                                && (double)bh / bw < 0.25;
                if (bottomStrip)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = new Box2d(bx, by, bw, bh);
                }
            }
        }

        return best;
    }

    /// <summary>Flood-fill фоновым значением (128) от всех пикселей по периметру изображения.</summary>
    private static void FillFromBorder(Mat bin, int cols, int rows)
    {
        var seeds = new List<OpenCvSharp.Point>();
        for (int x = 0; x < cols; x++) { seeds.Add(new OpenCvSharp.Point(x, 0)); seeds.Add(new OpenCvSharp.Point(x, rows - 1)); }
        for (int y = 1; y < rows - 1; y++) { seeds.Add(new OpenCvSharp.Point(0, y)); seeds.Add(new OpenCvSharp.Point(cols - 1, y)); }

        var lo = new Scalar(0); var hi = new Scalar(0);
        foreach (var pt in seeds)
        {
            byte pv = bin.At<byte>(pt.Y, pt.X);
            if (pv == 255)
                Cv2.FloodFill(bin, pt, new Scalar(128), out _, lo, hi, FloodFillFlags.Link4);
        }
    }

    // ----------------------------------------------------------------
    // Стратегия 2: connected components + ослабленный фильтр краёв
    // Убираем ТОЛЬКО тонкие горизонтальные полосы, касающиеся нижнего края (стол/пол)
    // ----------------------------------------------------------------
    private static Box2d? TryRelaxedComponentFilter(Mat blur, int cols, int rows)
    {
        Box2d? best = null;
        double bestArea = 0;
        double totalArea = (double)cols * rows;

        foreach (var invert in new[] { false, true })
        {
            using var bin = new Mat();
            Cv2.Threshold(blur, bin, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            if (invert)
                Cv2.BitwiseNot(bin, bin);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(7, 7));
            using var closed = new Mat();
            Cv2.MorphologyEx(bin, closed, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int n = Cv2.ConnectedComponentsWithStats(closed, labels, stats, centroids);

            for (int i = 1; i < n; i++)
            {
                int bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                int by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                int bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                int bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

                if (area < totalArea * 0.003 || area > totalArea * 0.92)
                    continue;

                // Фильтруем горизонтальные полосы у нижнего края (стол/плинтус)
                // Уже должны быть отсечены FindEffectiveRows, но на всякий случай оставляем фильтр
                bool bottomStrip = (by + bh >= rows - 2)
                                && bh < rows * 0.25
                                && (double)bh / bw < 0.45;
                if (bottomStrip)
                    continue;

                if (area > bestArea)
                {
                    bestArea = area;
                    best = new Box2d(bx, by, bw, bh);
                }
            }
        }

        return best;
    }

    // ----------------------------------------------------------------
    // Стратегия 3: градиент яркости + flood-fill от границы + union bbox
    // Работает для белых объектов на белом фоне, где OTSU даёт сбой.
    // Края фона (свет soft-box, кривая задника) связаны с границей и удаляются flood-fill.
    // Края товара — изолированы; берём UNION всех изолированных компонентов.
    // ----------------------------------------------------------------
    private static Box2d? TryGradientObjectDetection(Mat blur, int cols, int rows)
    {
        using var gradX = new Mat();
        using var gradY = new Mat();
        Cv2.Sobel(blur, gradX, MatType.CV_16S, 1, 0, ksize: 3);
        Cv2.Sobel(blur, gradY, MatType.CV_16S, 0, 1, ksize: 3);

        using var absGX = new Mat();
        using var absGY = new Mat();
        Cv2.ConvertScaleAbs(gradX, absGX);
        Cv2.ConvertScaleAbs(gradY, absGY);

        using var grad = new Mat();
        Cv2.AddWeighted(absGX, 0.5, absGY, 0.5, 0, grad);

        // Низкий порог: слабые края белой обуви на белом фоне
        using var edgeBin = new Mat();
        Cv2.Threshold(grad, edgeBin, 8, 255, ThresholdTypes.Binary);

        // Малая дилатация: соединяем близкие края одного объекта (швы, отверстия шнуровки)
        var smallK = Math.Max(7, Math.Min(cols, rows) / 55);
        using var kSmall = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(smallK, smallK));
        using var dilated = new Mat();
        Cv2.Dilate(edgeBin, dilated, kSmall);

        // Flood-fill от границы: удаляет края фона (свет, задник), связанные с границей кадра
        using var forFill = dilated.Clone();
        FillFromBorder(forFill, cols, rows);

        // Остаются только изолированные края — края товара
        using var isolated = new Mat();
        Cv2.InRange(forFill, new Scalar(255), new Scalar(255), isolated);

        // Для белой обуви могут остаться отдельные регионы (подошва, шнурки, лого).
        // Берём UNION bbox всех компонентов, отфильтровав шум.
        // Это даёт полный bbox товара, а не только одной его части.
        return UnionBboxOfComponents(isolated, cols, rows);
    }

    /// <summary>
    /// Возвращает bbox-объединение всех компонентов, игнорируя компоненты меньше порога.
    /// Используется для сборки bbox белого объекта из нескольких изолированных областей.
    /// </summary>
    private static Box2d? UnionBboxOfComponents(Mat mask, int cols, int rows)
    {
        double total = (double)cols * rows;

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var found = false;

        for (int i = 1; i < n; i++)
        {
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            // Отфильтровываем шум (< 0.08% площади), оставляем реальные части объекта
            if (area < total * 0.0008)
                continue;

            var bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            var by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            var bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

            minX = Math.Min(minX, bx);
            minY = Math.Min(minY, by);
            maxX = Math.Max(maxX, bx + bw);
            maxY = Math.Max(maxY, by + bh);
            found = true;
        }

        if (!found)
            return null;

        var w = maxX - minX;
        var h = maxY - minY;
        if ((double)w * h < total * 0.01 || (double)w * h > total * 0.85)
            return null;

        return new Box2d(minX, minY, w, h);
    }

    // ----------------------------------------------------------------
    // Вспомогательные
    // ----------------------------------------------------------------
    private static Box2d? LargestConnectedBbox(Mat mask, int cols, int rows, double minFrac, double maxFrac)
    {
        double total = (double)cols * rows;
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

        Box2d? best = null;
        double bestArea = 0;
        for (int i = 1; i < n; i++)
        {
            int bx = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            int by = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            int bw = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            int bh = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

            if (area < total * minFrac || area > total * maxFrac)
                continue;

            if (area > bestArea)
            {
                bestArea = area;
                best = new Box2d(bx, by, bw, bh);
            }
        }
        return best;
    }

    /// <summary>OpenCV-операции ниже ожидают BGR (3 канала).</summary>
    private static Mat EnsureBgr(Mat src)
    {
        if (src.Channels() == 3)
            return src.Clone();

        var dst = new Mat();
        if (src.Channels() == 1)
            Cv2.CvtColor(src, dst, ColorConversionCodes.GRAY2BGR);
        else if (src.Channels() == 4)
            Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
        else
            throw new InvalidOperationException($"Unsupported Mat channels: {src.Channels()}");
        return dst;
    }
}
