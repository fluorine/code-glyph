using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace CodeGlyph.Vsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("CodeGlyph", "Semantic watermark icons behind C# code", "0.1.0")]
    [ProvideOptionPage(typeof(ProviderOptionsPage), "CodeGlyph", "Provider", 0, 0, true)]
    [ProvideOptionPage(typeof(AppearanceOptionsPage), "CodeGlyph", "Appearance", 0, 0, true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(PackageGuidString)]
    public sealed class CodeGlyphPackage : AsyncPackage
    {
        public const string PackageGuidString = "5f3c8a1e-7b24-4d96-a0c5-3e1d92b7f410";
        private static readonly Guid CmdSet = new Guid("8c1d4e72-93ab-4f05-b6e8-2a7c5d09e3f1");

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            if (!(await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commands)) return;

            var toggle = new OleMenuCommand(
                (s, e) => GlyphServices.Update(x => x.Enabled = !x.Enabled),
                new CommandID(CmdSet, 0x0100));
            toggle.BeforeQueryStatus += (s, e) => ((OleMenuCommand)s).Checked = GlyphServices.Settings.Enabled;
            commands.AddCommand(toggle);

            commands.AddCommand(new MenuCommand(
                (s, e) => GlyphServices.ClearCache(),
                new CommandID(CmdSet, 0x0101)));

            commands.AddCommand(new MenuCommand(
                (s, e) => GlyphServices.ShowUsage(),
                new CommandID(CmdSet, 0x0102)));
        }
    }
}
