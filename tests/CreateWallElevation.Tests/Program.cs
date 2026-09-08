using CreateWallElevation;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Equal(double expected, double actual, double tolerance = 1e-8)
{
    if (Math.Abs(expected - actual) > tolerance || double.IsNaN(actual))
        throw new Exception($"Expected {expected}, actual {actual}");
}
void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
void Reject(Action run)
{
    try { run(); } catch (ArgumentException) { return; }
    throw new Exception("Invalid input was accepted");
}

foreach (var direction in new[] { new Point2(1, 0), new Point2(-1, 0), new Point2(0, 1), new Point2(0, -1) })
    Test($"Facade observes its wall from {direction.X},{direction.Y}", () =>
    {
        var surface = new Point2(10, 20);
        var frame = Geometry2D.CreateFrame(surface, direction, 0.3);
        Equal(0.3, (frame.Observer - surface).Length);
        Equal(1, frame.ObserverDirection.Dot((frame.Observer - surface).Normalized()));
        Equal(1, (frame.ObserverDirection * -1).Dot((surface - frame.Observer).Normalized()));
    });

Test("Arc offset is applied once to the true curve midpoint", () =>
{
    double angle = Math.PI / 40;
    var radial = new Point2(Math.Cos(angle), Math.Sin(angle));
    var frame = Geometry2D.CreateFrame(radial * 3000, radial * -1, 300);
    Equal(2700, frame.Observer.Length);
});
foreach (double traversal in new[] { 1.0, -1.0 })
foreach (double side in new[] { 1.0, -1.0 })
    Test($"Production curve pipeline: 270 degrees, traversal {traversal}, side {side}", () =>
    {
        double sweep = traversal * 3 * Math.PI / 2;
        Point2 At(double t) => new Point2(3000 * Math.Cos(t * sweep), 3000 * Math.Sin(t * sweep));
        Point2 Tangent(double t) => new Point2(-Math.Sin(t * sweep) * sweep, Math.Cos(t * sweep) * sweep);
        var segments = Geometry2D.PrepareSegments(At, Tangent, (p, t) => p.Normalized() * side,
            10, 100, 300, 500, p => true);
        Equal(10, segments.Count);
        foreach (var segment in segments)
        {
            Equal(3000 + side * 400, segment.Frame.Observer.Length);
            foreach (var p in segment.SurfacePoints) Equal(3000 + side * 100, p.Length);
            Equal(-1, segment.Frame.ObserverDirection.Dot(segment.Frame.SectionBoxDirection));
        }
        Equal(3000 + side * 100, segments[0].SurfacePoints[0].X);
        var end = segments.Last().SurfacePoints.Last();
        Equal(0, end.X); Equal(-traversal * (3000 + side * 100), end.Y);
    });
Test("Production curve pipeline rejects insufficient far depth", () =>
    Reject(() => Geometry2D.PrepareSegments(t => new Point2(t * 1000, 0), t => new Point2(1, 0),
        (p, t) => new Point2(0, 1), 1, 0, 300, 200, p => true)));
Test("Plan containment respects an internal island", () =>
{
    IList<IList<Point2>> loops = new List<IList<Point2>>
    {
        new[] { new Point2(0, 0), new Point2(6, 0), new Point2(6, 6), new Point2(0, 6) },
        new[] { new Point2(2, 2), new Point2(4, 2), new Point2(4, 4), new Point2(2, 4) }
    };
    Require(Geometry2D.IsInsideLoops(new Point2(1, 3), loops), "Interior point rejected");
    Require(!Geometry2D.IsInsideLoops(new Point2(3, 3), loops), "Island considered part of room");
    var inward = Geometry2D.InteriorNormal(new Point2(2, 3), new Point2(0, 1),
        p => Geometry2D.IsInsideLoops(p, loops), 0.1);
    Equal(-1, inward.X);
});
Test("Local normal enters right leg of U room regardless of anchor", () =>
{
    bool Contains(Point2 p) => p.X > 0 && p.X < 6 && p.Y > 0 && p.Y < 6 &&
        (p.Y < 2 || p.X < 2 || p.X > 4);
    var normal = Geometry2D.InteriorNormal(new Point2(4, 4), new Point2(0, -1), Contains, 0.1);
    Equal(1, normal.X); Equal(0, normal.Y);
});
Test("Local normal adapts to a narrow room", () =>
{
    var normal = Geometry2D.InteriorNormal(new Point2(0, 0), new Point2(0, 1),
        p => p.X > 0 && p.X < 0.003, 0.1);
    Equal(1, normal.X);
});
Test("Ambiguous room boundary is rejected", () =>
    Reject(() => Geometry2D.InteriorNormal(new Point2(0, 0), new Point2(1, 0), p => false, 0.1)));
Test("Four collinear 600mm parts survive a 1000mm threshold", () =>
{
    var parts = Enumerable.Range(0, 4).Select(i => new Segment2(new Point2(i * 600, 0), new Point2((i + 1) * 600, 0))).ToList();
    var result = Geometry2D.SimplifyLines(parts, 1000, 1, false);
    Equal(1, result.Count); Equal(2400, result[0].Length);
});
Test("Simplification never bridges a gap", () =>
{
    var parts = new[] { new Segment2(new Point2(0, 0), new Point2(1200, 0)),
        new Segment2(new Point2(1500, 0), new Point2(2700, 0)) };
    Equal(2, Geometry2D.SimplifyLines(parts, 1000, 1, false).Count);
});
Test("A short recess breaks a merge even when filtered out", () =>
{
    var points = new[] { new Point2(0, 0), new Point2(1200, 0), new Point2(1200, 200),
        new Point2(1400, 200), new Point2(1400, 0), new Point2(2600, 0) };
    var parts = Enumerable.Range(0, points.Length - 1).Select(i => new Segment2(points[i], points[i + 1])).ToList();
    var result = Geometry2D.SimplifyLines(parts, 1000, 1, false);
    Equal(2, result.Count); Equal(1200, result[0].Length); Equal(1200, result[1].Length);
});
Test("Closed-loop first and last parts merge before filtering", () =>
{
    var points = new[] { new Point2(600, 0), new Point2(1200, 0), new Point2(1200, 1200),
        new Point2(0, 1200), new Point2(0, 0), new Point2(600, 0) };
    var parts = Enumerable.Range(0, points.Length - 1).Select(i => new Segment2(points[i], points[i + 1])).ToList();
    Equal(4, Geometry2D.SimplifyLines(parts, 1000, 1, true).Count);
});
Test("Negative bottom offset exposes the floor below the original base", () =>
{
    var range = Geometry2D.VerticalRange(10, 13, -0.1, 0.2);
    Equal(9.9, range.Min); Equal(13.2, range.Max);
});
Test("Positive bottom offset raises the lower crop boundary", () =>
{
    var range = Geometry2D.VerticalRange(10, 13, 0.1, 0.2);
    Equal(10.1, range.Min); Equal(13.2, range.Max);
});
Test("Bottom offset cannot collapse or invert the resulting crop", () =>
{
    Reject(() => Geometry2D.VerticalRange(10, 13, 3.2, 0.2));
    Reject(() => Geometry2D.VerticalRange(10, 13, 4, 0.2));
    Reject(() => Geometry2D.VerticalRange(10, 13, double.NaN, 0));
    Reject(() => Geometry2D.VerticalRange(10, 13, double.NegativeInfinity, 0));
    Reject(() => Geometry2D.VerticalRange(0, double.MaxValue, 0, double.MaxValue));
});
Test("Reject negative/nonfinite offsets and invalid segment counts", () =>
{
    Reject(() => Geometry2D.CreateFrame(new Point2(0, 0), new Point2(1, 0), -1));
    Reject(() => Geometry2D.CreateFrame(new Point2(0, 0), new Point2(1, 0), double.NaN));
    Reject(() => Geometry2D.SegmentParameters(0));
    Reject(() => Geometry2D.SegmentParameters(1001));
    Reject(() => Geometry2D.VerticalRange(3, 1, 0, 0));
});
Test("Layout avoids an occupied viewport and stays within the sheet", () =>
{
    var bounds = new Rect2(0, 0, 10, 8);
    var occupied = new[] { new Rect2(0, 4, 4, 8) };
    Require(Geometry2D.TryPlace(4, 3, bounds, occupied, 0.2, out var rect), "No placement");
    Require(bounds.Contains(rect), "Outside the sheet");
    Require(!rect.Overlaps(occupied[0], 0.2), "Overlaps an existing view");
});
Test("Layout returns failure for an oversized view", () =>
    Require(!Geometry2D.TryPlace(11, 3, new Rect2(0, 0, 10, 8), Array.Empty<Rect2>(), 0.2, out _), "Oversized view was placed"));

Test("Numeric fields accept decimal comma and decimal point", () =>
{
    Equal(300.5, InputValues.Millimeters("300,5", "Отступ", false));
    Equal(300.5, InputValues.Millimeters("300.5", "Отступ", false));
});
Test("Numeric fields reject missing, nonfinite and negative values", () =>
{
    foreach (var text in new[] { "", "NaN", "Infinity", "-1", "1e999", "abc" })
        Reject(() => InputValues.Millimeters(text, "Глубина", true));
    Reject(() => InputValues.Millimeters("0", "Глубина", true));
    Reject(() => InputValues.Segments("0"));
    Reject(() => InputValues.Segments("1.5"));
});
Test("Signed bottom input accepts negative decimals, explicit plus and zero", () =>
{
    Equal(-100.5, InputValues.SignedMillimeters(" -100,5 ", "Низ"));
    Equal(-100.5, InputValues.SignedMillimeters("−100.5", "Низ"));
    Equal(100, InputValues.SignedMillimeters("+100", "Низ"));
    Equal(0, InputValues.SignedMillimeters("0", "Низ"));
    var range = Geometry2D.VerticalRange(1000, 4000, InputValues.SignedMillimeters("-100", "Низ"), 0);
    Equal(900, range.Min); Equal(4000, range.Max);
});
Test("Signed bottom input still rejects missing and nonfinite numbers", () =>
{
    foreach (var text in new[] { "", "NaN", "Infinity", "-Infinity", "1e999", "--100", "abc" })
        Reject(() => InputValues.SignedMillimeters(text, "Низ"));
    Reject(() => InputValues.Millimeters("-100", "Отступ от грани", false));
    Reject(() => InputValues.Millimeters("-100", "Верх", false));
});
foreach (bool sections in new[] { true, false })
{
    Test($"Empty prefix preserves existing names, sections={sections}", () =>
    {
        Require(ViewNaming.RoomPrefix("  ", sections, "101") == (sections ? "Р_П101" : "Ф_П101"), "Room default changed");
        Require(ViewNaming.WallPrefix(null, sections) == (sections ? "Р_Ст" : "Ф_Ст"), "Wall default changed");
    });
    Test($"Custom prefix replaces automatic portion, sections={sections}", () =>
    {
        Require(ViewNaming.RoomPrefix(" АР ", sections, "101") == "АР_101", "Room prefix not applied");
        Require(ViewNaming.RoomPrefix("АР", sections, "102") == "АР_102", "Room number was lost");
        Require(ViewNaming.WallPrefix(" АР ", sections) == "АР", "Wall prefix not applied");
    });
}
Test("View prefixes reject invalid characters before view creation", () =>
{
    foreach (char c in "\\:{}[]|;<>?`~\n\t")
        Reject(() => ViewNaming.NormalizePrefix("АР" + c + "01"));
    Require(ViewNaming.NormalizePrefix(" АР - Отделка_01 ") == "АР - Отделка_01", "Valid prefix rejected");
    Require(ViewNaming.RoomPrefix("АР", true, "1:2") == "АР_1_2", "Unsafe room number not sanitized");
});
Test("New settings use zero bottom offset by default", () =>
{
    var settings = new CreateWallElevationSettings();
    settings.Upgrade();
    Require(settings.IndentDown == "0", "Default lower offset is not zero");
    Require(settings.ViewNamePrefix == "", "Default naming changed");
});
Test("Old XML lower offset migrates once while preserving the crop", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "wall-elevation-test-" + Guid.NewGuid() + ".xml");
    const string oldXml = "<CreateWallElevationSettings><IndentDown>100,5</IndentDown><ProjectionDepth>0</ProjectionDepth></CreateWallElevationSettings>";
    try
    {
        File.WriteAllText(path, oldXml);
        var settings = SettingsStore.Load(path, null, () => new CreateWallElevationSettings());
        settings.Upgrade();
        Require(settings.UsesSignedBottomOffset, "Migration not marked");
        Equal(-100.5, InputValues.SignedMillimeters(settings.IndentDown, "Низ"));
        Require(settings.ProjectionDepth == "500", "Old depth migration lost");
        Require(File.ReadAllText(path) == oldXml, "Reading modified the old file");
        settings.Upgrade();
        Equal(-100.5, InputValues.SignedMillimeters(settings.IndentDown, "Низ"));
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path, null, () => new CreateWallElevationSettings());
        loaded.Upgrade();
        Equal(-100.5, InputValues.SignedMillimeters(loaded.IndentDown, "Низ"));
    }
    finally { File.Delete(path); }
});
foreach (string bottom in new[] { "-100,5", "+100", "0" })
    Test($"New XML preserves signed offset {bottom} and custom prefix", () =>
    {
        string path = Path.Combine(Path.GetTempPath(), "wall-elevation-test-" + Guid.NewGuid() + ".xml");
        try
        {
            SettingsStore.Save(path, new CreateWallElevationSettings
            { UsesSignedBottomOffset = true, IndentDown = bottom, ViewNamePrefix = "АР - Отделка" });
            var settings = SettingsStore.Load(path, null, () => new CreateWallElevationSettings());
            settings.Upgrade();
            Require(settings.IndentDown == bottom, "Signed value changed on reload");
            Require(settings.ViewNamePrefix == "АР - Отделка", "Custom prefix lost on reload");
        }
        finally { File.Delete(path); }
    });
Test("Settings recover from corrupt XML without changing the old file", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "wall-elevation-test-" + Guid.NewGuid() + ".xml");
    try
    {
        File.WriteAllText(path, "<broken");
        var settings = SettingsStore.Load(path, null, () => new TestSettings { Value = "default" });
        Require(settings.Value == "default", "Defaults were not restored");
        Require(File.ReadAllText(path) == "<broken", "Loading changed the settings file");
    }
    finally { File.Delete(path); }
});
Test("Settings migrate a legacy file and replace the user file atomically", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "wall-elevation-test-" + Guid.NewGuid() + ".xml");
    string legacy = path + ".legacy";
    try
    {
        SettingsStore.Save(legacy, new TestSettings { Value = "legacy" });
        Require(SettingsStore.Load(path, legacy, () => new TestSettings()).Value == "legacy", "Legacy read failed");
        SettingsStore.Save(path, new TestSettings { Value = "first" });
        SettingsStore.Save(path, new TestSettings { Value = "second" });
        Require(SettingsStore.Load(path, legacy, () => new TestSettings()).Value == "second", "User settings were not preferred");
        Require(SettingsStore.Load(legacy, null, () => new TestSettings()).Value == "legacy", "Legacy file changed");
    }
    finally { File.Delete(path); File.Delete(legacy); }
});

int failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;

public class TestSettings { public string Value { get; set; } }
