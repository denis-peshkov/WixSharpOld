//css_dir ..\..\;
//css_ref Wix_bin\SDK\Microsoft.Deployment.WindowsInstaller.dll;
//css_ref System.Core.dll;

using System;
using System.Xml;
using Microsoft.Win32;
using System.Windows.Forms;
using WixSharp;

class Script
{
    static public void Main(string[] args)
    {
        try
        {
            Project project =
                new Project("My Product",

                    new Dir(@"%ProgramFiles%\My Company\My Product",
                        new Dir("Samples",
                            new File(@"AppFiles\MyApp.cs")),
                        new File(@"AppFiles\MyApp.exe",
                            new FileShortcut("MyApp", "INSTALLDIR"), //INSTALLDIR is the ID of "%ProgramFiles%\My Company\My Product" 
                            new FileShortcut("MyApp", @"%Desktop%") { IconFile = @"AppFiles\Icon.ico" }),
                        new ExeFileShortcut("Uninstall MyApp", "[System64Folder]msiexec.exe", "/x [ProductCode]")),

                    new Dir(@"%ProgramMenu%\My Company\My Product",
                        new ExeFileShortcut("Samples", @"[" + @"%ProgramFiles%\My Company\My Product\Samples".ToDirID()+"]", ""),
                        new ExeFileShortcut("Uninstall MyApp", "[System64Folder]msiexec.exe", "/x [ProductCode]")));

            project.GUID = new Guid("6fe30b47-2577-43ad-9095-1861ba25889b"); 
            project.UI = WUI.WixUI_ProgressOnly;
            project.OutFileName = "setup";

            Compiler.BuildMsi(project);
        }
        catch (System.Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}