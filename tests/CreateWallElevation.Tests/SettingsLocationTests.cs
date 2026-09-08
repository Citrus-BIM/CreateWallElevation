using System;
using System.IO;
using System.Reflection;
using CreateWallElevation;

internal static class SettingsLocationTests
{
    public static void Register(Action<string, Action> test)
    {
        test("Ordinary settings follow the loaded DLL in the module root or a versioned bin directory", () =>
            InTemporaryDirectory(root =>
            {
                foreach (string folder in new[] { root, Path.Combine(root, "CreateWallElevation", "bin", "R2026") })
                {
                    var location = SettingsLocation.BesideAssembly(Path.Combine(folder, "CreateWallElevation.dll"),
                        Path.Combine(root, "UserProfile"));
                    Require(location.FilePath == Path.Combine(folder, "CreateWallElevationSettings.xml"),
                        "Settings must remain beside the actual DLL, including a versioned build output.");
                }
            }));

        test("Ordinary production provider follows its executing assembly", () =>
        {
            string expected = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "CreateWallElevationSettings.xml");
            Require(SettingsLocationProvider.GetLocation().FilePath == expected,
                "The ordinary adapter must select the assembly directory rather than the user profile.");
        });

        test("Spectrum location works without any assembly location", () =>
            InTemporaryDirectory(root =>
            {
                var location = SettingsLocation.InUserProfile(root);
                Require(location.FilePath == Path.Combine(root, "Citrus BIM", "CreateWallElevation", "CreateWallElevationSettings.xml"),
                    "Spectrum must need only LocalApplicationData, including when Assembly.Location is empty.");
                Require(location.MigrationFilePath == null, "Spectrum must never probe the assembly directory.");
            }));

        test("Ordinary settings reject missing or relative assembly locations instead of using the working directory", () =>
            InTemporaryDirectory(root =>
            {
                foreach (string location in new[] { null, "", " ", "CreateWallElevation.dll", @"bin\CreateWallElevation.dll", @"C:CreateWallElevation.dll", @"\CreateWallElevation.dll" })
                    Reject(() => SettingsLocation.BesideAssembly(location, root));
            }));

        test("Spectrum settings reject missing profile locations instead of using the working directory", () =>
        {
            foreach (string profile in new[] { null, "", " ", "UserProfile", @"C:UserProfile" })
                Reject(() => SettingsLocation.InUserProfile(profile));
        });

        test("Ordinary adjacent settings work when the optional migration profile is unavailable", () =>
            InTemporaryDirectory(root =>
            {
                foreach (string profile in new[] { null, "", " ", "UserProfile", @"C:UserProfile" })
                {
                    var location = SettingsLocation.BesideAssembly(Path.Combine(root, "CreateWallElevation.dll"), profile);
                    Require(location.MigrationFilePath == null, "An unavailable migration profile must be skipped.");
                    location.Save(Settings("Ordinary"));
                    Require(location.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Ordinary",
                        "A valid ordinary location must not depend on optional AppData migration.");
                }
            }));

        test("Ordinary settings prefer an existing adjacent XML over the previous shared profile", () =>
            InTemporaryDirectory(root =>
            {
                var ordinary = Ordinary(root);
                var spectrum = SettingsLocation.InUserProfile(Path.Combine(root, "UserProfile"));
                spectrum.Save(Settings("Spectrum"));
                ordinary.Save(Settings("Ordinary"));
                Require(ordinary.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Ordinary",
                    "An existing file beside the DLL must take precedence over migration data.");
            }));

        test("Ordinary settings migrate the shared profile without changing it and then reload their own XML", () =>
            InTemporaryDirectory(root =>
            {
                var ordinary = Ordinary(root);
                var spectrum = SettingsLocation.InUserProfile(Path.Combine(root, "UserProfile"));
                spectrum.Save(Settings("Previous"));
                byte[] original = File.ReadAllBytes(spectrum.FilePath);
                var loaded = ordinary.Load(() => new CreateWallElevationSettings());
                Require(loaded.ViewNamePrefix == "Previous", "The previous profile must remain a migration source.");
                Require(!File.Exists(ordinary.FilePath), "Reading must not create a new file.");
                loaded.ViewNamePrefix = "Migrated";
                ordinary.Save(loaded);
                Require(ordinary.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Migrated",
                    "After saving, the next launch must read the ordinary file.");
                Require(Convert.ToBase64String(File.ReadAllBytes(spectrum.FilePath)) == Convert.ToBase64String(original),
                    "Migration must leave the old shared XML byte-for-byte unchanged.");
            }));

        test("Ordinary and Spectrum writes remain isolated across repeated saves", () =>
            InTemporaryDirectory(root =>
            {
                var ordinary = Ordinary(root);
                var spectrum = SettingsLocation.InUserProfile(Path.Combine(root, "UserProfile"));
                ordinary.Save(Settings("Ordinary 1"));
                spectrum.Save(Settings("Spectrum 1"));
                ordinary.Save(Settings("Ordinary 2"));
                Require(spectrum.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Spectrum 1",
                    "An ordinary save must not change the Spectrum profile.");
                spectrum.Save(Settings("Spectrum 2"));
                Require(ordinary.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Ordinary 2",
                    "A Spectrum save must not change ordinary settings.");
                Require(spectrum.Load(() => new CreateWallElevationSettings()).ViewNamePrefix == "Spectrum 2",
                    "Spectrum must reload its own latest save.");
            }));

        test("Spectrum does not migrate XML beside an ordinary DLL", () =>
            InTemporaryDirectory(root =>
            {
                Ordinary(root).Save(Settings("Ordinary"));
                var spectrum = SettingsLocation.InUserProfile(Path.Combine(root, "UserProfile"));
                Require(spectrum.Load(() => Settings("Default")).ViewNamePrefix == "Default",
                    "A missing Spectrum profile must use defaults rather than ordinary settings.");
            }));
    }

    private static SettingsLocation Ordinary(string root) => SettingsLocation.BesideAssembly(
        Path.Combine(root, "Module", "bin", "R2026", "CreateWallElevation.dll"), Path.Combine(root, "UserProfile"));

    private static CreateWallElevationSettings Settings(string prefix) => new CreateWallElevationSettings
    {
        ViewNamePrefix = prefix,
        UsesSignedBottomOffset = true
    };

    private static void InTemporaryDirectory(Action<string> run)
    {
        string root = Path.Combine(Path.GetTempPath(), "wall-elevation-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { run(root); }
        finally { Directory.Delete(root, true); }
    }

    private static void Reject(Action run)
    {
        try { run(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An unavailable absolute storage location must be rejected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
