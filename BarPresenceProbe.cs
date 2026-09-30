namespace ClickyBot;

internal readonly record struct StaminaBarReading(bool? Visible, int FillPercent);

// Match the stable left rim of the stamina bar, independent of its fill and number.
internal sealed class BarPresenceProbe
{
    private readonly MacroRule _rule;
    private readonly ResourceNavigationSettings _settings;
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private StaminaBarReading _cachedReading;
    private DateTime _cachedAt = DateTime.MinValue;

    private BarPresenceProbe(MacroRule rule, ResourceNavigationSettings settings)
    {
        _rule = rule;
        _settings = settings;
    }

    public static BarPresenceProbe Create(ResourceNavigationSettings settings)
    {
        if (settings.BarReferenceWidth is < 1 or > 1200
            || settings.BarReferenceHeight is < 1 or > 800
            || (long)settings.BarReferenceWidth * settings.BarReferenceHeight > ScreenProbe.MaxReferencePixels
            || settings.BarCropX < 0 || settings.BarCropY < 0
            || settings.BarCropWidth is < 8 or > 200 || settings.BarCropHeight is < 8 or > 100
            || settings.BarCropX + settings.BarCropWidth > settings.BarReferenceWidth
            || settings.BarCropY + settings.BarCropHeight > settings.BarReferenceHeight
            || settings.BarFillStartX < 0 || settings.BarFillRowY < 0
            || settings.BarFillWidth is < 10 or > 1200
            || settings.BarFillRowY >= settings.BarCropHeight
            || settings.BarCropX + settings.BarFillStartX + settings.BarFillWidth > settings.BarReferenceWidth
            || settings.LowFillPercent is < 0 or > 50
            || settings.HighFillPercent is < 50 or > 100
            || settings.LowFillPercent >= settings.HighFillPercent
            || settings.BarMatchThreshold is < 50 or > 100
            || settings.BarSearchWidth is < 1 or > ScreenProbe.MaxSearchWidth
            || settings.BarSearchHeight is < 1 or > ScreenProbe.MaxSearchHeight
            || settings.BarCropWidth > settings.BarSearchWidth
            || settings.BarCropHeight > settings.BarSearchHeight
            || settings.BarMissingMs is < 500 or > 30000
            || settings.BarCheckIntervalMs is < 100 or > 5000)
            throw new InvalidOperationException("Stamina bar recovery settings are incomplete or out of range.");

        if (!ReferenceImageService.TryLoadRgb(settings.BarReferenceImagePath,
                settings.BarReferenceWidth, settings.BarReferenceHeight, out var fullImage))
            throw new InvalidOperationException("The stamina bar reference could not be loaded. Check its path and dimensions in the macro JSON.");

        var crop = new byte[settings.BarCropWidth * settings.BarCropHeight * 3];
        for (var row = 0; row < settings.BarCropHeight; row++)
        {
            var source = ((settings.BarCropY + row) * settings.BarReferenceWidth + settings.BarCropX) * 3;
            Array.Copy(fullImage, source, crop, row * settings.BarCropWidth * 3, settings.BarCropWidth * 3);
        }

        return new BarPresenceProbe(new MacroRule
        {
            Name = "Stamina bar edge",
            Condition = ConditionType.RegionSnapshotMatches,
            SearchReference = true,
            ImageMatchMethod = ImageMatchMethod.ImageSimilarity,
            WatchWidth = settings.BarCropWidth,
            WatchHeight = settings.BarCropHeight,
            ReferenceRgb = crop,
            CoverageThreshold = settings.BarMatchThreshold,
            SearchX = settings.BarSearchX,
            SearchY = settings.BarSearchY,
            SearchWidth = settings.BarSearchWidth,
            SearchHeight = settings.BarSearchHeight
        }, settings);
    }

    // Shared by the stamina controller and watchdog so they do not scan the same frame twice.
    public async Task<StaminaBarReading> ReadAsync(CancellationToken token)
    {
        await _readGate.WaitAsync(token);
        try
        {
            if ((DateTime.UtcNow - _cachedAt).TotalMilliseconds < 150)
                return _cachedReading;
            _cachedReading = await Task.Run(() => ReadCore(token), token);
            _cachedAt = DateTime.UtcNow;
            return _cachedReading;
        }
        finally
        {
            _readGate.Release();
        }
    }

    private StaminaBarReading ReadCore(CancellationToken token)
    {
        var location = ScreenProbe.FindReference(_rule, token);
        if (_rule.LastImageScore is null)
            return new StaminaBarReading(null, 0); // Capture failed; absence is unproven.
        if (location is null)
            return new StaminaBarReading(false, 0);

        var x = location.Value.X - _settings.BarCropWidth / 2 + _settings.BarFillStartX;
        var y = location.Value.Y - _settings.BarCropHeight / 2 + _settings.BarFillRowY;
        if (!ScreenProbe.TryCaptureRegion(x, y, _settings.BarFillWidth, 1, out var row))
            return new StaminaBarReading(null, 0);

        var filled = 0;
        for (var column = 0; column < _settings.BarFillWidth; column++)
        {
            var offset = column * 3;
            var red = row[offset];
            var green = row[offset + 1];
            var blue = row[offset + 2];
            if (red > green + 4 && green > blue + 6 && red > 50)
                filled++;
        }
        return new StaminaBarReading(true, (int)Math.Round(filled * 100d / _settings.BarFillWidth));
    }
}
