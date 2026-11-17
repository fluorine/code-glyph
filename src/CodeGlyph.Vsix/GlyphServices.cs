using System;
using System.Net.Http;
using CodeGlyph.Selectors;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CodeGlyph.Vsix
{
    internal static class GlyphServices
    {
        private static readonly object Gate = new object();
        private static readonly Guid PaneGuid = new Guid("b4d8f0a2-6c31-4e75-9a18-7f3e2c5d1b96");
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private static GlyphSettings _settings = SettingsStore.Load();
        private static IconCatalog? _catalog;
        private static IconCache? _cache;
        private static SelectionPipeline? _pipeline;
        private static IVsOutputWindowPane? _pane;

        public static event Action? SettingsChanged;

        public static GlyphSettings Settings
        {
            get { lock (Gate) return _settings; }
        }

        public static SelectionPipeline Pipeline
        {
            get
            {
                lock (Gate)
                {
                    if (_pipeline == null)
                    {
                        _catalog = IconCatalog.LoadEmbedded();
                        _cache = new IconCache(SettingsStore.CachePath);
                        _pipeline = new SelectionPipeline(
                            _catalog,
                            _cache,
                            () => SelectorFactory.Create(Settings.Provider, Http),
                            () => Settings.Style,
                            Log,
                            null,
                            () => Settings.LogUsage);
                    }
                    return _pipeline;
                }
            }
        }

        public static IconCatalog Catalog
        {
            get
            {
                _ = Pipeline;
                return _catalog!;
            }
        }

        public static void Update(Action<GlyphSettings> change)
        {
            lock (Gate)
            {
                change(_settings);
                try { SettingsStore.Save(_settings); }
                catch (Exception ex) { Log("Cannot save settings: " + ex.Message); }
            }
            SettingsChanged?.Invoke();
        }

        public static void ClearCache()
        {
            _ = Pipeline;
            _cache!.Clear();
            SettingsChanged?.Invoke();
        }

        public static void ShowUsage()
        {
            Log(Pipeline.Usage.Summary(), activate: true);
        }

        public static void Log(string message) => Log(message, false);

        private static void Log(string message, bool activate)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_pane == null)
                {
                    var window = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
                    if (window == null) return;
                    var guid = PaneGuid;
                    window.CreatePane(ref guid, "CodeGlyph", 1, 1);
                    window.GetPane(ref guid, out _pane);
                }
                _pane?.OutputStringThreadSafe(DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
                if (activate) _pane?.Activate();
            }).Task.FileAndForget("codeglyph/log");
        }
    }
}
