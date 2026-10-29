using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Desktop_Frames;
using Newtonsoft.Json.Linq;

// A dependency-free Windows regression runner against the actual application assembly.
// No App startup, desktop frames, registry writes, or message loop are started.
internal static class Program
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static int assertions;

    [STAThread]
    private static void Main()
    {
        SettingsPersistence();
        SeasonalStartup();
        AppearanceResources();
        Console.WriteLine($"PASS: {assertions} assertions (settings, dates, startup state, localized WPF controls).");
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static FieldInfo Field(Type type, string name) => type.GetField(name, PrivateStatic)!;
    private static object? State(string name) => Field(typeof(InterCore), name).GetValue(null);
    private static void Start(DateTime date) => typeof(InterCore)
        .GetMethod("InitializeSeasonalEffects", PrivateStatic)!.Invoke(null, new object[] { date });

    private static void ResetSeason()
    {
        foreach (string timer in new[] { "_winterTimer", "_halloweenTimer" })
        {
            (State(timer) as DispatcherTimer)?.Stop();
            Field(typeof(InterCore), timer).SetValue(null, null);
        }
        foreach (string flag in new[] { "_isWinterActive", "_isNewYearActive", "_isHalloweenActive" })
            Field(typeof(InterCore), flag).SetValue(null, false);
        Field(typeof(InterCore), "_seasonalEffectsEnabled").SetValue(null, null);
    }

    private static void SettingsPersistence()
    {
        Check(SettingsManager.EnableSeasonalEffects, "Fresh process default must preserve seasonal effects.");
        string root = Path.Combine(AppContext.BaseDirectory, "seasonal-test-" + Guid.NewGuid());
        string master = Path.Combine(AppContext.BaseDirectory, "MasterOptions.json");
        if (File.Exists(master)) throw new InvalidOperationException("Run from an isolated test output directory without MasterOptions.json.");
        Field(typeof(ProfileManager), "_profilesRootDir").SetValue(null, root);
        string options = ProfileManager.GetProfileFilePath("options.json");
        try
        {
            foreach (string? oldConfig in new string?[] { null, "", "{}", "null", "{broken", "{\"TintValue\":42}", "{\"EnableSeasonalEffects\":null}", "{\"EnableSeasonalEffects\":{}}" })
            {
                if (oldConfig == null) File.Delete(options);
                else File.WriteAllText(options, oldConfig);
                SettingsManager.EnableSeasonalEffects = false; // Previous profile's value.
                SettingsManager.LoadSettings();
                Check(SettingsManager.EnableSeasonalEffects, $"Default fallback failed for {oldConfig ?? "missing file"}.");
                Check((bool)JObject.Parse(File.ReadAllText(options))["EnableSeasonalEffects"]!, "Hydration must write true.");
                if (oldConfig == "{\"TintValue\":42}") Check(SettingsManager.TintValue == 42, "Existing setting changed.");
            }
            foreach (bool enabled in new[] { false, true })
            {
                SettingsManager.EnableSeasonalEffects = enabled;
                SettingsManager.SaveSettings();
                Check((bool)JObject.Parse(File.ReadAllText(options))["EnableSeasonalEffects"]! == enabled, "Saved value mismatch.");
                SettingsManager.EnableSeasonalEffects = !enabled;
                SettingsManager.LoadSettings();
                Check(SettingsManager.EnableSeasonalEffects == enabled, "Reload lost explicit setting.");
            }
            File.WriteAllText(master, "{\"EnableSeasonalEffects\":false}");
            string localBefore = File.ReadAllText(options);
            SettingsManager.LoadSettings();
            Check(!SettingsManager.EnableSeasonalEffects, "MasterOptions must take precedence.");
            SettingsManager.EnableSeasonalEffects = true;
            SettingsManager.SaveSettings();
            Check((bool)JObject.Parse(File.ReadAllText(master))["EnableSeasonalEffects"]!, "Master save failed.");
            Check(File.ReadAllText(options) == localBefore, "Master mode wrote the local profile.");
            File.WriteAllText(master, "{}");
            SettingsManager.EnableSeasonalEffects = false;
            SettingsManager.LoadSettings();
            Check(SettingsManager.EnableSeasonalEffects, "Legacy master default failed.");
        }
        finally
        {
            File.Delete(master);
            Directory.Delete(root, true);
        }
    }

    private static void SeasonalStartup()
    {
        for (DateTime day = new DateTime(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            foreach (bool enabled in new[] { false, true })
            {
                ResetSeason();
                SettingsManager.EnableSeasonalEffects = enabled;
                Start(day);
                bool newYear = enabled && day.Month == 1 && day.Day <= 2;
                bool winter = newYear || (enabled && day.Month == 12 && day.Day >= 22 && day.Day <= 29);
                Check((bool)State("_isWinterActive")! == winter, $"Winter flag: {day:d}, {enabled}");
                Check((bool)State("_isNewYearActive")! == newYear, $"New Year flag: {day:d}, {enabled}");
                Check(!(bool)State("_isHalloweenActive")!, $"Release 2.7.8.377's Halloween suspension must hold: {day:d}, {enabled}");
                Check((State("_winterTimer") is DispatcherTimer { IsEnabled: true }) == winter, "Winter timer state.");
                Check(State("_halloweenTimer") == null, "Opting in must not allocate a suspended Halloween timer.");
                if (!enabled)
                {
                    Check(State("_winterTimer") == null && State("_halloweenTimer") == null, "Disabled startup allocated seasonal timers.");
                    foreach (string collection in new[] { "_winterContexts", "_halloweenContexts", "_snowflakes", "_fireworkComets", "_fireworkSparks", "_confetti", "_glitter", "_embers" })
                        Check(((ICollection)State(collection)!).Count == 0, $"Disabled startup populated {collection}.");
                }
            }
        }
        foreach (DateTime day in new[] { new DateTime(2026, 1, 1), new DateTime(2026, 12, 22) })
        {
            ResetSeason();
            SettingsManager.EnableSeasonalEffects = false;
            Start(day);
            SettingsManager.EnableSeasonalEffects = true;
            Start(day);
            Check(State("_winterTimer") == null && State("_halloweenTimer") == null, "Enabling applied before restart.");
            ResetSeason();
            Start(day);
            object? timer = State("_winterTimer") ?? State("_halloweenTimer");
            SettingsManager.EnableSeasonalEffects = false;
            Start(day);
            Check(ReferenceEquals(timer, State("_winterTimer") ?? State("_halloweenTimer")), "Saving replaced the running timer.");
            Check(((DispatcherTimer)timer!).IsEnabled, "Disabling applied before restart.");
        }
        ResetSeason();
        SettingsManager.EnableSeasonalEffects = false;
        InterCore.Initialize(); // Actual public entry point, without pumping the registry timer.
        Check(State("_winterTimer") == null && State("_halloweenTimer") == null, "Public startup bypassed the gate.");
        Check(State("_registryMonitor") is DispatcherTimer { IsEnabled: true }, "Unrelated registry listener was disabled.");
        InterCore.Cleanup();
    }

    private static void AppearanceResources()
    {
        Type strings = typeof(InterCore).Assembly.GetType("Desktop_Frames.Localization.Strings")!;
        var cultureProperty = strings.GetProperty("OverrideCulture")!;
        var get = strings.GetMethod("Get", new[] { typeof(string) })!;
        var resources = new ResourceManager("Desktop_Frames.Localization.Strings", typeof(InterCore).Assembly);
        foreach (string culture in new[] { "en", "de", "el", "es", "fr", "it", "ja", "nl", "pl", "pt", "ru", "zh-Hans", "ar" })
        {
            cultureProperty.SetValue(null, CultureInfo.GetCultureInfo(culture));
            string label = (string)get.Invoke(null, new object[] { "OptEnableSeasonalEffects" })!;
            string note = (string)get.Invoke(null, new object[] { "NoteSeasonalEffectsRestart" })!;
            Check(label.Length > 0 && label != "OptEnableSeasonalEffects", "Missing label resource.");
            Check(note.Length > 0 && note != "NoteSeasonalEffectsRestart", "Missing restart resource.");
            // Check the actual bundled resource set, without allowing parent-language
            // fallback to hide a missing translation. Arabic exercises fallback separately.
            if (culture != "ar")
            {
                var resourceCulture = culture == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
                var resourceSet = resources.GetResourceSet(resourceCulture, true, false);
                Check(!string.IsNullOrWhiteSpace(resourceSet?.GetString("OptEnableSeasonalEffects")), $"Missing bundled label: {culture}");
                Check(!string.IsNullOrWhiteSpace(resourceSet?.GetString("NoteSeasonalEffectsRestart")), $"Missing bundled restart hint: {culture}");
            }
            foreach (bool enabled in new[] { false, true })
            {
                SettingsManager.EnableSeasonalEffects = enabled;
                var tabs = new TabControl();
                Field(typeof(OptionsFormManager), "_tabControl").SetValue(null, tabs);
                typeof(OptionsFormManager).GetMethod("CreateAppearanceTab", PrivateStatic)!.Invoke(null, null);
                var panel = (StackPanel)((ScrollViewer)((TabItem)tabs.Items[0]).Content).Content;
                var checkbox = panel.Children.OfType<CheckBox>().Single(c => c.Name == "EnableSeasonalEffects");
                Check(checkbox.IsChecked == enabled && (string)checkbox.Content == label, "Checkbox did not load saved state/localized text.");
                var hint = panel.Children.OfType<TextBlock>().Single(t => t.Text == note);
                Check(hint.TextWrapping == TextWrapping.Wrap, "Restart hint must wrap for translations.");
                panel.Measure(new Size(560, double.PositiveInfinity));
                panel.Arrange(new Rect(0, 0, 560, panel.DesiredSize.Height));
                Check(checkbox.ActualHeight > 0 && hint.ActualHeight > 0, "Seasonal controls did not lay out.");
            }
        }
        cultureProperty.SetValue(null, null);
    }
}
