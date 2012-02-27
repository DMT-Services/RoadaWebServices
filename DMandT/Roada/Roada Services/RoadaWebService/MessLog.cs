using System;
using System.Collections;
using System.IO;
using System.Security.Permissions;
using System.Reflection;

namespace DMTServices
{
    /// <summary>
    /// Message Log class, derived from ArrayList class
    /// Automatically creates a seperate log file for each day of week (filename1 .. 7.log)
    /// </summary>
    public class MessLog : ArrayList
    {
        const string DATEFORMAT = "HH:mm:ss.fff dd/MM/yy";

        public int maxItems = 250;    // default of 250 items before auto-flushing
                                      //    set to 1 to flush each message
                                      //           0 to never auto flush...
        private string fLogFileName;
        private string fLogPath;
        private string OpenFile;
        // various settings
        private bool OverWrite = false;     // clear the file when starting a new day
        private bool OnePerDay = false;     // save one for each day of the week
        private bool FullMonth = false;     // or for each day of the month
        private bool noStartMsg = false;    // don't put the 'Start Log' when class first created...
        
        // use to decide when to create a new file for the new day...
        private int LDayOfWeek = -1;
        // should we add in the version # message?
        private bool AddVersion = true;

        private void startMessL()
        {
            if (fLogFileName.Length == 0)
                fLogFileName = "Messages.log";

            if (!(fLogFileName.StartsWith(@"\") || fLogFileName.Substring(1, 1) == ":"))
                fLogFileName = ApplicationPath() + fLogFileName;

            // get the full path into the file name, so we won't get 'confused'
            // if the program changes the default dir...
            fLogFileName = Path.GetFullPath(fLogFileName);
            fLogPath = Path.GetDirectoryName(fLogFileName);

            if (OnePerDay)
                OpenFile = GetDtFileName();
            else
            {
                OpenFile = fLogFileName;
                if (OverWrite && myFile.Exists(OpenFile))
                    myFile.Delete(OpenFile);
            }

            // make the log directory, if it's not already there...
            try
            {
                if (!myDirectory.Exists(Path.GetDirectoryName(OpenFile)))
                    myDirectory.Create(Path.GetDirectoryName(OpenFile));
            }
            catch
            {
                // no exceptions...
            }

            if (!noStartMsg)
                Add("Start Log");
            AddVersion = !noStartMsg;
        }

        public MessLog(string FileName)
            : base()
        {
            fLogFileName = FileName;
            OverWrite = true;
            OnePerDay = true;
            FullMonth = false;
            noStartMsg = false;

            startMessL();
        }

        public MessLog(string FileName, bool noLeadMsg)
            : base()
        {
            fLogFileName = FileName;
            OverWrite = true;
            OnePerDay = true;
            FullMonth = false;
            noStartMsg = noLeadMsg;

            startMessL();
        }

        public MessLog(string FileName, bool OverWr, bool OnePD, bool KeepMonth)
            : base()
        {
            fLogFileName = FileName;
            FullMonth = KeepMonth;
            OverWrite = OverWr;
            OnePerDay = OnePD;
            noStartMsg = false;

            startMessL();
        }

        public MessLog(string FileName, bool OverWr, bool OnePD, bool KeepMonth, bool noLeadMsg)
            : base()
        {
            fLogFileName = FileName;
            FullMonth = KeepMonth;
            OverWrite = OverWr;
            OnePerDay = OnePD;
            noStartMsg = noLeadMsg;
            startMessL();
        }

        // C# version of destroy...
        ~MessLog()
        {
            if (!noStartMsg)
                Flush("End Log");
            else
                Flush("");
        }

        public override int Add(object mess)
        {
            int rc = base.Add(DateTime.Now.ToString(DATEFORMAT) + " - " + mess);
            if (maxItems > 0)
                if (Count >= maxItems)
                {
                    iFlush("");
                    rc = 0;
                }
            return rc;
        }

        public void Flush(string msg, params object[] args)
        {
            iFlush(string.Format(msg, args));
        }

        public void Flush(string msg)
        {
            iFlush(msg);
        }

        public void Flush()
        {
            iFlush("");
        }

        private void iFlush(string msg)
        {
            // add in any message passed to this function...
            if (msg.Length > 0)
                Add(msg);

            if (Count > 0)
            {
                int nDayOfWeek;
                // A 'new' log file each day...
                if (OnePerDay)
                {
                    if (FullMonth)
                        nDayOfWeek = DateTime.Now.Day;
                    else
                        nDayOfWeek = (int)DateTime.Now.DayOfWeek + 1;       // Sunday is 0, make it 1

                    if (LDayOfWeek != nDayOfWeek)
                    {
                        OpenFile = GetDtFileName();
                        if (OverWrite)
                        {
                            if (myFile.Exists(OpenFile))
                                myFile.Delete(OpenFile);
                            AddVersion = !noStartMsg;
                        }
                    }
                }

                try
                {
                    // open the file for read/write and allow others to share it too...
                    FileStream fsw = new FileStream(OpenFile, FileMode.OpenOrCreate);//, FileAccess.ReadWrite);//, FileShare.ReadWrite);
                    try
                    {
                        // create the 'writer' to the file...
                        StreamWriter sw = new StreamWriter(fsw);
                        try
                        {
                            // go to end of file...
                            fsw.Seek(0, SeekOrigin.End);

                            if (AddVersion)
                            {
                                try
                                {
                                    sw.WriteLine(".....................  Version - " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
                                }
                                catch { }
                                AddVersion = false;
                            }
                            
                            for (int i = 0; i < Count; i++)
                                sw.WriteLine(this[i]);
                            // clear out the ArrayList...
                            this.Clear();
                        }
                        //catch (Exception e)
                        //{
                        //    Add("Ex - sw: " + e.Message);
                        //}
                        finally
                        {
                            sw.Close();
                        }
                    }
                    //catch (Exception e)
                    //{
                    //    Add("Ex - fsw: " + e.Message);
                    //}
                    finally
                    {
                        fsw.Close();
                    }
                }
                catch // (Exception e)
                {
                      // Add("OverAll -" + e.Message);
                }
            }
        }

        public string GetDtFileName()
        {
            string ext = Path.GetExtension(fLogFileName);
            if (FullMonth)
                LDayOfWeek =  DateTime.Now.Day;
            else
                LDayOfWeek = (int)DateTime.Now.DayOfWeek + 1;       // Sunday is 0, make it 1
            return fLogPath + "\\" + Path.GetFileNameWithoutExtension(fLogFileName) + LDayOfWeek.ToString() + ext;
        }

        /// <summary>
        /// Return the application .exe path
        /// </summary>
        static public string ApplicationPath()
        {
            string Result = System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase;

            // change all forward slashes to back slashes (if we are a web site above returns /'s)
            int p = Result.LastIndexOf("/");
            if (p >= 0)
                Result = Result.Replace('/', '\\');

            // when web type program you can get this 'weirdness'...
            if (Result.ToLower().StartsWith("file:"))
            {
                Result = Result.Substring(5);
                p = Result.IndexOf(":");
                if (p >= 0)
                    Result = Result.Substring(p - 1);
            }

            // strip .exe name
            p = Result.LastIndexOf(@"\");
            if (p == -1)
                Result = "";
            else
                Result = Result.Remove(p + 1, Result.Length - p - 1);   // strip the .exe name

            // when we are a web site it's the dll on the bin directory, so move up one from there...
            if (Result.ToLower().EndsWith(@"\bin\"))
                Result = Result.Substring(0, Result.Length - 4);

            return Result;
        }

    }
}
