namespace IMRCRaceControl.Models;

public sealed class RentalTransponderState
{
    public List<RentalTransponder> Inventory { get; set; } = [];
    public List<RentalTransponderUse> History { get; set; } = [];
    public double LabelFontSize { get; set; } = 18;
    public double RowHeight { get; set; } = 42;
}

public sealed class RentalTransponder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Number { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Color { get; set; } = "Default";
    public int LabelNumber { get; set; }
    public string LabelHex => Color switch
    {
        "Red" => "#EF6471", "Black" => "#181818", "Blue" => "#65AAFF", "Green" => "#54C895",
        "Yellow" => "#D9B231", "Orange" => "#EC9957", "Purple" => "#BC91EC", "Pink" => "#E989C3",
        _ => "#96A7BA"
    };
}

public sealed class RentalTransponderUse
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RentalId { get; set; } = "";
    public string TransponderNumber { get; set; } = "";
    public string RentalNickname { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public string DriverId { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? ReturnedAt { get; set; }
    public string Status => ReturnedAt is null ? "Assigned" : "Returned / past event";
}
