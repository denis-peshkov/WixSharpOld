//css_dir ..\..\;
//css_ref Wix_bin\SDK\Microsoft.Deployment.WindowsInstaller.dll;
//css_ref System.Core.dll;
using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using WixSharp;
using WixSharp.CommonTasks;
using File=WixSharp.File;

class Script
{
    static public void Main(string[] args)
    {
        Environment.SetEnvironmentVariable("bin", @"Files\Bin");
        Environment.SetEnvironmentVariable("docs", @"Files\Docs");
        Environment.SetEnvironmentVariable("LATEST_RELEASE", Environment.CurrentDirectory);

        var project =
            new Project("MyProduct",

                new Dir(@"%ProgramFiles%\My Company\My Product",
                    new File(@"%bin%\MyApp.exe"),
                    new Dir(@"Docs\Manual",
                        new File(@"%docs%\Manual.txt"))),

                new EnvironmentVariable("MYPRODUCT_DIR", "[INSTALLDIR]"),
                new EnvironmentVariable("PATH", "[INSTALLDIR]") { Part = EnvVarPart.last });

        project.GUID = new Guid("6f330b47-2577-43ad-9095-1861ba25889b");
        project.UI = WUI.WixUI_InstallDir;
        project.RemoveDialogsBetween(Dialogs.WelcomeDlg, Dialogs.InstallDirDlg);

        project.OutDir = @"%LATEST_RELEASE%\MSI";
        project.SourceBaseDir = "%LATEST_RELEASE%";
        project.BuildMsi();
    }
}



