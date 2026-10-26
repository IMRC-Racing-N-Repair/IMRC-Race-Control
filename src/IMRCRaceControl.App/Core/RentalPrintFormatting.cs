using IMRCRaceControl.Models;

namespace IMRCRaceControl.Core;
public sealed record ReportTextStyle(double FontSize, bool Bold, string Color);

public sealed class RentalPrintFormatting
{
    private readonly RentalTransponderState? _state;
    private readonly RaceReport _report;
    private readonly Dictionary<string, RentalTransponder> _inventory;
    public RentalPrintFormatting(RentalTransponderState? state, RaceReport report)
    {
        _state = state; _report = report;
        _inventory = (state?.Inventory ?? []).ToDictionary(r => r.Number.TrimStart('0'), StringComparer.Ordinal);
    }
    public string Format(string value)
    {
        if (_state is null || _inventory.Count == 0) return value;
        return string.Join("; ", RentalTransponderRegistry.Split(value).Select(number =>
        {
            if (!_inventory.TryGetValue(number.TrimStart('0'), out var rental)) return number;
            var label = _state.PrintFormat switch
            {
                "Number" => $"Rental: {rental.Number}",
                "NicknameAndNumber" => $"Rental: {rental.Nickname} ({rental.Number})",
                _ => $"Rental: {rental.Nickname}"
            };
            var color = _state.PrintInColor ? PrintColor(rental.Color) : "#000000";
            _report.TextStyles[label] = new ReportTextStyle(_state.PrintFontSize, _state.PrintBold, color);
            return label;
        }));
    }
    // Darker ink colors remain legible on white paper, including yellow labels.
    public static string PrintColor(string color) => color switch
    {
        "Red" => "#A51C29", "Blue" => "#174E9E", "Green" => "#17673C", "Yellow" => "#806000",
        "Orange" => "#994800", "Purple" => "#64368A", "Pink" => "#9F2768", _ => "#000000"
    };
}
