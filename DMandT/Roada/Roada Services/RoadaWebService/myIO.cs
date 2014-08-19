using System;
using System.IO;
using System.Security.AccessControl;
#if WINFORMS
using System.Windows.Forms;
#endif

namespace DMTServices
{
    /// <summary>
    /// File class, just with try / catch to prevent exceptions...
    /// </summary>
    public static class myFile
    {
        static public bool Exists(string Filename)
        {
            return Exists(Filename, false);
        }

        static public bool Exists(string Filename, bool showMsg)
        {
            bool r = false;
            try
            {
                r = File.Exists(Filename);
            }
#if WINFORMS
            catch (Exception ex)
            {
                if (showMsg)
                    MessageBox.Show(ex.Message);
            }
#else
            catch { }
#endif

            return r;
        }

        static public void Delete(string Filename)
        {
            Delete(Filename, false);
        }

        static public void Delete(string Filename, bool showMsg)
        {
            try
            {
                File.Delete(Filename);
            }
#if WINFORMS
            catch(Exception ex)
            {
                if(showMsg)
                    MessageBox.Show(ex.Message);
            }
#else
            catch { }
#endif
        }

        static public void Move(string fFilename, string tFilename)
        {
            Move(fFilename, tFilename, false);
        }

        static public string Move(string fFilename, string tFilename, bool mkUnique)
        {
            if (mkUnique)
            {
                int ct = 1;
                string justFile = Path.GetFileNameWithoutExtension(tFilename);
                while (myFile.Exists(tFilename))
                    tFilename = Path.GetDirectoryName(tFilename) + "\\" + justFile + "_" + ct++.ToString() + Path.GetExtension(tFilename);
            }
            try
            {
                File.Move(fFilename, tFilename);
            }
            catch
            {
            }
            return tFilename;
        }

        static public FileStream Create(string Filename)
        {
            FileStream r = null;
            try
            {
                r = File.Create(Filename);
            }
            catch
            {
            }
            return r;
        }
    }

    /// <summary>
    ///  Directory class, just with try / catch to prevent exceptions...
    /// </summary>
    public static class myDirectory
    {
        static public bool Exists(string DirectoryName)
        {
            bool r = false;
            try
            {
                r = Directory.Exists(DirectoryName);
            }
            catch
            {
            }
            return r;
        }

        static public void Delete(string DirectoryName)
        {
            try
            {
                Directory.Delete(DirectoryName);
            }
            catch
            {
            }
        }

        public static string[] GetFiles(string path)
        {
            string[] f = new string[0];
            try
            {
                f = Directory.GetFiles(path);
            }
            catch
            {
            }
            return f;
        }

        public static string[] GetFiles(string path, string searchPattern)
        {
            string[] f = new string[0];
            try
            {
                f = Directory.GetFiles(path, searchPattern);
            }
            catch
            {
            }
            return f;
        }

        static public DirectoryInfo Create(string DirectoryName)
        {
            DirectoryInfo r = null;
            try
            {
                r = Directory.CreateDirectory(DirectoryName);
            }
            catch
            {
            }
            return r;
        }

        static public DirectoryInfo Create(string DirectoryName, DirectorySecurity di)
        {
            DirectoryInfo r = null;
            try
            {
                r = Directory.CreateDirectory(DirectoryName, di);
            }
            catch { }
            //(Exception e)
            //{
            //    Global.messLog.Flush("Create(" + DirectoryName + ") - " + e.Message);
            //}
            return r;
        }

        static public DirectoryInfo CreateDirectory(string DirectoryName, DirectorySecurity di)
        {
            return Create(DirectoryName, di);
        }

        static public DirectoryInfo CreateDirectory(string DirectoryName)
        {
            return Create(DirectoryName);
        }

        static private DirectorySecurity addASPNet(DirectorySecurity ds)
        {
            FileSystemRights fsr = FileSystemRights.Modify | FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory | FileSystemRights.Read | FileSystemRights.Write;
            try
            {
                // this one is needed to allow the website to read/write/modify a directory
                ds.AddAccessRule(new FileSystemAccessRule("IIS_WPG", fsr, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }
            catch { }
            return ds;
        }

        static public void addASPNet(string DirectoryName)
        {
            // Get a DirectorySecurity object that represents the current security settings.
            DirectorySecurity dSecurity = Directory.GetAccessControl(DirectoryName);

            // Add the FileSystemAccessRule to the security settings. 
            dSecurity = addASPNet(dSecurity);

            try
            {
                // Set the new access settings.
                Directory.SetAccessControl(DirectoryName, dSecurity);
            }
            catch { }
            //(Exception e)
            //{
            //    Global.messLog.Flush("addASPNet(" + DirectoryName + ") - " + e.Message);
            //}
        }

        /// <summary>
        /// create a 'writeable' (by asp.net/IIS) directory...
        /// </summary>
        /// <param name="DirectoryName">full path to directory</param>
        /// <returns>DirectoryInfo of created directory</returns>
        static public DirectoryInfo MakeWriteableDir(string DirectoryName)
        {
            DirectoryInfo rc;
            if (!Exists(DirectoryName))
            {
                DirectorySecurity ds = new DirectorySecurity();
                try
                {
                    // this should allow the administrator user to access (FullControl)...
                    ds.AddAccessRule(new FileSystemAccessRule("Administrators", FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                }
                catch { }
                rc = CreateDirectory(DirectoryName, addASPNet(ds));
            }
            else
            {
                addASPNet(DirectoryName);
                rc = new DirectoryInfo(DirectoryName);
            }
            return rc;
        }
    }

    public static class Export
    {
        public static string ExportCSV(System.Data.DataTable data, string aPath)
        {
            string fn = System.IO.Path.GetTempFileName();
            if (!aPath.EndsWith("\\"))
                aPath = aPath + "\\";
            string fileName = System.IO.Path.GetFileNameWithoutExtension(fn) + ".csv";
            try
            {
                if (myFile.Exists(aPath + "\\" + fileName))
                    myFile.Delete(aPath + "\\" + fileName);

                // open / create the file...
                FileStream fsw = new FileStream(aPath + "\\" + fileName, FileMode.OpenOrCreate);//, FileAccess.ReadWrite);//, FileShare.ReadWrite);
                try
                {
                    // create the 'writer' to the file...
                    StreamWriter sw = new StreamWriter(fsw);
                    try
                    {
                        // write column header names
                        string line = string.Empty;

                        for (int i = 0; i < data.Columns.Count; i++)
                        {
                            if (i > 0)
                                line += ",";
                            line += data.Columns[i].ColumnName;
                        }
                        sw.WriteLine(line);

                        // Write data
                        foreach (System.Data.DataRow row in data.Rows)
                        {
                            line = string.Empty;
                            for (int i = 0; i < data.Columns.Count; i++)
                            {
                                if (i > 0)
                                    line += ",";
                                line += row[i];
                            }
                            sw.WriteLine(line);
                        }
                    }
                    finally
                    {
                        sw.Close();
                    }
                }
                finally
                {
                    fsw.Close();
                }
            }
            catch // (Exception e)
            {
                // e.Message);
            }
            return fileName;
        }
    }
}
