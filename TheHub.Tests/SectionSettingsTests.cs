using TheHub.Common.Config;
using Xunit;

namespace TheHub.Tests;

public class SectionSettingsTests
{
    [Fact]
    public void SettingsMigration_FromGoldenFile_CreatesAllSectionsAndKeepsMigrated()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TheHubTests_Migration_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings.golden.json");
            string targetLegacy = Path.Combine(tempDir, "settings.json");
            File.Copy(fixturePath, targetLegacy);

            var store = new SectionSettingsStore(tempDir);
            var logs = new List<string>();
            store.LoadAll(msg => logs.Add(msg));

            // Legacy settings.json must be renamed to settings.json.migrated
            Assert.False(File.Exists(targetLegacy), "Legacy settings.json must no longer exist directly");
            Assert.True(File.Exists(targetLegacy + ".migrated"), "Legacy settings.json.migrated must be preserved");

            // All section files must exist
            Assert.True(File.Exists(Path.Combine(tempDir, "app.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "wallpaper.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "island.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "radial.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "workspaces.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "widgets.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "assistant.json")));

            // Check loaded values
            Assert.True(store.Radial.RadialLauncherEnabled);
            Assert.True(store.Radial.LauncherShortcuts.Count > 0);
            Assert.True(store.Workspaces.Workspaces.Count > 0);
            Assert.True(store.Wallpaper.Wallpapers.Count > 0);
            Assert.Equal(2500, store.Island.IslandDurationMs);

            // Round-trip through ToAppSettings
            var appSettings = store.ToAppSettings();
            Assert.NotNull(appSettings);
            Assert.Equal(store.Radial.RadialRadius, appSettings.RadialRadius);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SectionSettings_CorruptedSection_FallsBackToDefaultsWithoutAffectingOthers()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "TheHubTests_Corrupt_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var store = new SectionSettingsStore(tempDir);

            // Write valid radial section
            var radial = new RadialSection { RadialRadius = 180, GlobalShortcut = "Ctrl+Space" };
            store.SaveSectionAtomic("radial.json", radial);

            // Write corrupted island section
            File.WriteAllText(Path.Combine(tempDir, "island.json"), "{ invalid json: true, ");

            var logs = new List<string>();
            store.LoadAll(msg => logs.Add(msg));

            // Radial must have loaded correctly
            Assert.Equal(180, store.Radial.RadialRadius);
            Assert.Equal("Ctrl+Space", store.Radial.GlobalShortcut);

            // Corrupted Island must have fallen back to default without crash
            Assert.Equal(2500, store.Island.IslandDurationMs);
            Assert.Contains(logs, l => l.Contains("Corrupted section file 'island.json'"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
