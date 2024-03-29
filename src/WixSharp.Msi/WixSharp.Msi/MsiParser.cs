using System;
using System.Text;
using System.Text.RegularExpressions;
using WindowsInstaller;

namespace WixSharp.UI
{
    /// <summary>
    /// Utility class for simplifying MSI interpreting tasks DB querying, message data parsing  
    /// </summary>
    public class MsiParser
    {
        string msiFile;
        IntPtr db;

        /// <summary>
        /// Opens the specified MSI file and returns the database handle.
        /// </summary>
        /// <param name="msiFile">The msi file.</param>
        /// <returns>Handle to the MSI database.</returns>
        public static IntPtr Open(string msiFile)
        {
            IntPtr db = IntPtr.Zero;
            MsiExtensions.Invoke(() => MsiInterop.MsiOpenDatabase(msiFile, MsiDbPersistMode.ReadOnly, out db));
            return db;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MsiParser" /> class.
        /// </summary>
        /// <param name="msiFile">The msi file.</param>
        public MsiParser(string msiFile)
        {
            this.msiFile = msiFile;
            this.db = MsiParser.Open(msiFile);
        }

        /// <summary>
        /// Queries the name of the product from the encapsulated MSI database.
        /// <para>
        /// <remarks>The DB view is not closed after the call</remarks>
        /// </para>
        /// </summary>
        /// <returns>Product name.</returns>
        public string GetProductName()
        {
            return this.db.View("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductName'")
                          .NextRecord()
                          .GetString(1);
        }
        /// <summary>
        /// Queries the version of the product from the encapsulated MSI database.
        /// <para>
        /// <remarks>The DB view is not closed after the call</remarks>
        /// </para>
        /// </summary>
        /// <returns>Product version.</returns>
        public string GetProductVersion()
        {
            return this.db.View("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'")
                          .NextRecord()
                          .GetString(1);
        }
        /// <summary>
        /// Queries the code of the product from the encapsulated MSI database.
        /// <para>
        /// <remarks>The DB view is not closed after the call</remarks>
        /// </para>
        /// </summary>
        /// <returns>Product code.</returns>
        public string GetProductCode()
        {
            return this.db.View("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductCode'")
                          .NextRecord()
                          .GetString(1);
        }

        /// <summary>
        /// Determines whether the specified product code is installed.
        /// </summary>
        /// <param name="productCode">The product code.</param>
        /// <returns>Returns <c>true</c> if the product is installed. Otherwise returns <c>false</c>.</returns>
        public static bool IsInstalled(string productCode)
        {
            StringBuilder sb = new StringBuilder(2048);
            uint size = 2048;
            MsiError err = MsiInterop.MsiGetProductInfo(productCode, MsiInstallerProperty.InstallDate, sb, ref size);

            if (err == MsiError.UnknownProduct)
                return false;
            else if (err == MsiError.NoError)
                return true;
            else
                throw new Exception(err.ToString());
        }

        /// <summary>
        /// Determines whether the product from the encapsulated msi file is installed.
        /// </summary>
        /// <returns>Returns <c>true</c> if the product is installed. Otherwise returns <c>false</c>.</returns>
        public bool IsInstalled()
        {
            return IsInstalled(this.GetProductCode());
        }

        /// <summary>
        /// Parses the <c>MsiInstallMessage.CommonData</c> data.
        /// </summary>
        /// <param name="s">Message data.</param>
        /// <returns>Collection of parsed tokens (fields).</returns>
        public static string[] ParseCommonData(string s)
        {
            //Example: 1: 0 2: 1033 3: 1252 
            var res = new string[3];
            var regex = new Regex(@"\d:\s?\w+\s");

            int i = 0;

            foreach (Match m in regex.Matches(s))
            {
                if (i > 3) return null;

                res[i++] = m.Value.Substring(m.Value.IndexOf(":") + 1).Trim();
            }

            return res;
        }

        /// <summary>
        /// Parses the <c>MsiInstallMessage.Progress</c> string.
        /// </summary>
        /// <param name="s">Message data.</param>
        /// <returns>Collection of parsed tokens (fields).</returns>
        public static string[] ParseProgressString(string s)
        {
            //1: 0 2: 86 3: 0 4: 1 
            var res = new string[4];
            var regex = new Regex(@"\d:\s\d+\s");

            int i = 0;

            foreach (Match m in regex.Matches(s))
            {
                if (i > 4) return null;

                res[i++] = m.Value.Substring(m.Value.IndexOf(":") + 2).Trim();
            }

            return res;
        }
    }
}