using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace DMTServices
{
    /// <summary>
    /// Message Log class, derived from TList class (thread safe ICollection, defined below) -   NOT ArrayList class
    /// Automatically creates a seperate log file for each day of week (filename1 .. 7.log)
    /// or 'optionally' one for each day of the month
    /// </summary>
    public class MessLog : TList<string>
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

        public int AddToLast(string mess)
        {
            if (Count > 0)
                try
                {
                    base[Count - 1] += mess;
                }
                catch { }
            else
                Add(mess);

            return Count;
        }

#if ! ARRAYLIST
        public new void Add(string mess)
        {
            try
            {
                base.Add(DateTime.Now.ToString(DATEFORMAT) + " - " + mess);
            }
            catch { }
            if (maxItems > 0 && Count >= maxItems)
                iFlush(string.Empty);
        }
#else
        public override int Add(object mess)
        {
            int rc = 0;
            try
            {
                rc = base.Add(DateTime.Now.ToString(DATEFORMAT) + " - " + mess);
            }
            catch { }
            if (maxItems > 0)
                if (Count >= maxItems)
                {
                    iFlush("");
                    rc = 0;
                }
            return rc;
        }
#endif

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
            iFlush(string.Empty);
        }

        private void iFlush(string msg)
        {
            // add in any message passed to this function...
            try
            {
                if (msg != string.Empty)
                    try
                    {
                        base.Add(DateTime.Now.ToString(DATEFORMAT) + " - " + msg);
                    }
                    catch { }

                if (Count > 0)
                {
                    // get the data out of the TList and clear it
                    string[] items = ToArray();
                    // clear out the ArrayList...
                    Clear();

                    checkDateChange();

                    try
                    {
                        // open the file for read/write and allow others to share it too...
                        using (FileStream fsw = new FileStream(OpenFile, FileMode.OpenOrCreate))  //, FileAccess.ReadWrite);//, FileShare.ReadWrite);
                        {
                            // create the 'writer' to the file...
                            using (StreamWriter sw = new StreamWriter(fsw))
                            {
                                // go to end of file...
                                fsw.Seek(0, SeekOrigin.End);

                                if (AddVersion)
                                {
                                    // don't let exceptions here stop the 'real' logging...
                                    try
                                    {
                                        sw.WriteLine(".....................  Version - " + Assembly.GetExecutingAssembly().GetName().Version.ToString());
                                    }
                                    catch { }
                                    AddVersion = false;
                                }

                                for (int i = 0; i < items.Length; i++)
                                    sw.WriteLine(items[i]);
                            }
                        }
                    }
                    catch // (Exception e)
                    {
                        // Add("OverAll -" + e.Message);
                    }
                }
            }
            catch { }
        }

        public string GetDtFileName()
        {
            string ext = Path.GetExtension(fLogFileName);
            if (FullMonth)
                LDayOfWeek = DateTime.Now.Day;
            else
                LDayOfWeek = (int)DateTime.Now.DayOfWeek + 1;       // Sunday is 0, make it 1
            return fLogPath + "\\" + Path.GetFileNameWithoutExtension(fLogFileName) + LDayOfWeek.ToString() + ext;
        }

        private void checkDateChange()
        {
            // A 'new' log file each day?
            if (OnePerDay)
            {
                int nDayOfWeek;
                if (FullMonth)
                    nDayOfWeek = DateTime.Now.Day;
                else
                    nDayOfWeek = (int)DateTime.Now.DayOfWeek + 1;       // Sunday is 0, make it 1

                if (LDayOfWeek != nDayOfWeek)
                {
                    OpenFile = GetDtFileName();         // this sets the LDayOfWeek value...
                    if (OverWrite)
                    {
                        if (myFile.Exists(OpenFile))
                            myFile.Delete(OpenFile);
                        AddVersion = !noStartMsg;
                    }
                }
            }
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

            // newer Visual Studio uses bin\Release or bin\Debug for WinForms...
            if (Result.ToLower().EndsWith(@"\debug\"))
                Result = Result.Substring(0, Result.Length - 6);
            if (Result.ToLower().EndsWith(@"\release\"))
                Result = Result.Substring(0, Result.Length - 8);
            // when we are a web site it's the dll on the bin directory, so move up one from there...
            if (Result.ToLower().EndsWith(@"\bin\"))
                Result = Result.Substring(0, Result.Length - 4);

            return Result;
        }
    }

    /// <summary>
    /// Represents a threaded List<>
    /// </summary>
    public class TList<T> : ICollection<T>
    {
        #region Variables

        /// <summary>
        /// The container list that holds the actual data
        /// </summary>
        private readonly List<T> m_TList;

        /// <summary>
        /// The lock used when accessing the list
        /// </summary>
        private readonly ReaderWriterLockSlim LockList = new ReaderWriterLockSlim();

        // Variables
        #endregion

        #region Properties

        private int m_Disposed;
        /// <summary>
        /// When true, we have already disposed of the object
        /// </summary>
        private bool Disposed
        {
            get { return Thread.VolatileRead(ref m_Disposed) == 1; }
            set { Thread.VolatileWrite(ref m_Disposed, value ? 1 : 0); }
        }

        // Properties
        #endregion

        #region Init

        /// <summary>
        /// Creates an empty threaded list object
        /// </summary>
        public TList()
        {
            m_TList = new List<T>();
        }

        /// <summary>
        /// Creates an empty threaded list object
        /// </summary>
        /// <param name="capacity">the number of elements the list can initially store</param>
        public TList(int capacity)
        {
            m_TList = new List<T>(capacity);
        }

        /// <summary>
        /// Creates an empty threaded list object
        /// </summary>
        /// <param name="collection">a collection of objects which are copied into the threaded list</param>
        public TList(IEnumerable<T> collection)
        {
            m_TList = new List<T>(collection);
        }

        // Init
        #endregion

        #region IEnumerable Members

        /// <summary>
        /// Returns an enumerator to iterate through the collection
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            // http://www.csharphelp.com/archives/archive181.html

            List<T> localList;

            // init enumerator
            LockList.EnterReadLock();
            try
            {
                // create a copy of m_TList
                localList = new List<T>(m_TList);
            }
            finally
            {
                LockList.ExitReadLock();
            }

            // get the enumerator
            foreach (T item in localList)
                yield return item;
        }

        #endregion

        #region IEnumerable<T> Members

        /// <summary>
        /// Returns an enumerator to iterate through the collection
        /// </summary>
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            // http://www.csharphelp.com/archives/archive181.html

            List<T> localList;

            // init enumerator
            LockList.EnterReadLock();
            try
            {
                // create a copy of m_TList
                localList = new List<T>(m_TList);
            }
            finally
            {
                LockList.ExitReadLock();
            }

            // get the enumerator
            foreach (T item in localList)
                yield return item;
        }

        #endregion

        #region IDisposable Members

        // Implement IDisposable.
        // Do not make this method virtual.
        // A derived class should not be able to override this method.
        /// <summary>
        /// Dispose of the object
        /// </summary>
        public void Dispose()
        {
            Dispose(true);

            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        // Dispose(bool disposing) executes in two distinct scenarios.
        // If disposing equals true, the method has been called directly
        // or indirectly by a user's code. Managed and unmanaged resources
        // can be disposed.
        // If disposing equals false, the method has been called by the
        // runtime from inside the finalizer and you should not reference
        // other objects. Only unmanaged resources can be disposed.
        private void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (this.Disposed) return;

            // If disposing equals true, dispose all managed
            // and unmanaged resources.
            if (disposing)
            {
                // Dispose managed resources.
            }

            // Note disposing has been done.
            Disposed = true;
        }

        // Use C# destructor syntax for finalization code.
        // This destructor will run only if the Dispose method
        // does not get called.
        // It gives your base class the opportunity to finalize.
        // Do not provide destructors in types derived from this class.
        ~TList()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        #endregion

        #region Add

        /// <summary>
        /// Adds an item to the threaded list
        /// </summary>
        /// <param name="item">the item to add to the end of the collection</param>
        public void Add(T item)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Add(item);
            }

            finally
            {
                LockList.ExitWriteLock();
            }
        }

        // Add
        #endregion

        #region AddRange

        /// <summary>
        /// Adds the elements of collection to the end of the threaded list
        /// </summary>
        /// <param name="collection">the collection to add to the end of the list</param>
        public void AddRange(IEnumerable<T> collection)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.AddRange(collection);
            }

            finally
            {
                LockList.ExitWriteLock();
            }
        }

        // AddRange
        #endregion

        #region AddIfNotExist

        /// <summary>
        /// Adds an item to the threaded list if it is not already in the list.
        /// Returns true if added to the list, false if the item already existed
        /// in the list
        /// </summary>
        /// <param name="item">the item to add to the end of the collection</param>
        public bool AddIfNotExist(T item)
        {
            LockList.EnterWriteLock();
            try
            {
                // check if it exists already
                if (m_TList.Contains(item))
                    return false;

                // add the item and return true
                m_TList.Add(item);
                return true;
            }

            finally
            {
                LockList.ExitWriteLock();
            }
        }

        // AddIfNotExist
        #endregion

        #region AsReadOnly

        /// <summary>
        /// Returns a read-only collection of the current threaded list
        /// </summary>
        public ReadOnlyCollection<T> AsReadOnly()
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.AsReadOnly();
            }

            finally
            {
                LockList.ExitReadLock();
            }
        }

        // AsReadOnly
        #endregion

        #region BinarySearch

        /// <summary>
        /// Searches the collection using the default comparator and returns the zero-based index of the item found
        /// </summary>
        /// <param name="item">the item to search for</param>
        public int BinarySearch(T item)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.BinarySearch(item);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Searches the collection using the default comparator and returns the zero-based index of the item found
        /// </summary>
        /// <param name="item">the item to search for</param>
        /// <param name="comparer">the IComparer to use when searching, or null to use the default</param>
        public int BinarySearch(T item, IComparer<T> comparer)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.BinarySearch(item, comparer);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Searches the collection using the default comparator and returns the zero-based index of the item found
        /// </summary>
        /// <param name="index">the zero-based index to start searching from</param>
        /// <param name="count">the number of records to search</param>
        /// <param name="item">the item to search for</param>
        /// <param name="comparer">the IComparer to use when searching, or null to use the default</param>
        public int BinarySearch(int index, int count, T item, IComparer<T> comparer)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.BinarySearch(index, count, item, comparer);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // BinarySearch
        #endregion

        #region Capacity

        /// <summary>
        /// Gets or sets the initial capacity of the list
        /// </summary>
        public int Capacity
        {
            get
            {
                LockList.EnterReadLock();
                try
                {
                    return m_TList.Capacity;
                }

                finally
                {
                    LockList.ExitReadLock();
                }
            }
            set
            {
                LockList.EnterWriteLock();
                try
                {
                    m_TList.Capacity = value;
                }

                finally
                {
                    LockList.ExitWriteLock();
                }
            }
        }

        // Capacity
        #endregion

        #region Clear

        /// <summary>
        /// Removes all items from the threaded list
        /// </summary>
        public void Clear()
        {
            LockList.EnterReadLock();
            try
            {
                m_TList.Clear();
            }

            finally
            {
                LockList.ExitReadLock();
            }
        }

        // Clear
        #endregion

        #region Contains

        /// <summary>
        /// Returns true if the collection contains this item
        /// </summary>
        /// <param name="item">the item to find in the collection</param>
        public bool Contains(T item)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.Contains(item);
            }

            finally
            {
                LockList.ExitReadLock();
            }
        }

        // Contains
        #endregion

        #region ConvertAll

        /// <summary>
        /// Converts the elements of the threaded list to another type, and returns a list of the new type
        /// </summary>
        /// <typeparam name="TOutput">the destination type</typeparam>
        /// <param name="converter">delegate to convert the items to a new type</param>
        public List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.ConvertAll(converter);
            }

            finally
            {
                LockList.ExitReadLock();
            }
        }

        // ConvertAll
        #endregion

        #region CopyTo

        /// <summary>
        /// Copies the elements of this threaded list to a one-dimension array of the same type
        /// </summary>
        /// <param name="array">the destination array</param>
        /// <param name="arrayIndex">index at which copying begins</param>
        public void CopyTo(T[] array, int arrayIndex)
        {
            LockList.EnterReadLock();
            try
            {
                m_TList.CopyTo(array, arrayIndex);
            }

            finally
            {
                LockList.ExitReadLock();
            }
        }

        // CopyTo
        #endregion

        #region Count

        /// <summary>
        /// Returns a count of the number of elements in this collection
        /// </summary>
        public int Count
        {
            get
            {
                LockList.EnterReadLock();
                try
                {
                    return m_TList.Count;
                }

                finally
                {
                    LockList.ExitReadLock();
                }
            }
        }

        // Count
        #endregion

        #region Exists

        /// <summary>
        /// Determines whether an item exists which meets the match criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public bool Exists(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.Exists(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // Exists
        #endregion

        #region Find

        /// <summary>
        /// Searches for an element that matches the criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public T Find(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.Find(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // Find
        #endregion

        #region FindAll

        /// <summary>
        /// Searches for elements that match the criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public List<T> FindAll(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindAll(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // FindAll
        #endregion

        #region FindIndex

        /// <summary>
        /// Returns the index of the element which matches the criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindIndex(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindIndex(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the index of the element which matches the criteria
        /// </summary>
        /// <param name="startIndex">the zero-based index starting the search</param>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindIndex(int startIndex, Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindIndex(startIndex, match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the index of the element which matches the criteria
        /// </summary>
        /// <param name="startIndex">the zero-based index starting the search</param>
        /// <param name="count">the number of elements to search</param>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindIndex(int startIndex, int count, Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindIndex(startIndex, count, match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // FindIndex
        #endregion

        #region FindLast

        /// <summary>
        /// Searches for the last element in the collection that matches the criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public T FindLast(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindLast(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // FindLast
        #endregion

        #region FindLastIndex

        /// <summary>
        /// Returns the last index of the element which matches the criteria
        /// </summary>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindLastIndex(Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindLastIndex(match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the last index of the element which matches the criteria
        /// </summary>
        /// <param name="startIndex">the zero-based index starting the search</param>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindLastIndex(int startIndex, Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindLastIndex(startIndex, match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the last index of the element which matches the criteria
        /// </summary>
        /// <param name="startIndex">the zero-based index starting the search</param>
        /// <param name="count">the number of elements to search</param>
        /// <param name="match">delegate that defines the conditions to search for</param>
        public int FindLastIndex(int startIndex, int count, Predicate<T> match)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.FindLastIndex(startIndex, count, match);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        // FindLastIndex
        #endregion

        #region ForEach

        /// <summary>
        /// Peforms the action on each element of the list
        /// </summary>
        /// <param name="action">the action to perfom</param>
        public void ForEach(Action<T> action)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.ForEach(action);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        // ForEach
        #endregion

        /// <summary>
        /// Creates a shallow copy of the range of elements in the source
        /// </summary>
        /// <param name="index">index to start from</param>
        /// <param name="count">number of elements to return</param>
        /// <returns></returns>
        public List<T> GetRange(int index, int count)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.GetRange(index, count);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        public T this[int ndx]
        {
            get
            {
                LockList.EnterReadLock();
                try
                {
                    return m_TList[ndx];
                }
                finally
                {
                    LockList.ExitReadLock();
                }
            }
            set
            {
                LockList.EnterReadLock();
                try
                {
                    m_TList[ndx] = value;
                }
                finally
                {
                    LockList.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Searches the list and returns the index of the item found in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        public int IndexOf(T item)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.IndexOf(item);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Searches the list and returns the index of the item found in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        /// <param name="index">the zero-based index to begin searching from</param>
        public int IndexOf(T item, int index)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.IndexOf(item, index);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Searches the list and returns the index of the item found in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        /// <param name="index">the zero-based index to begin searching from</param>
        /// <param name="count">the number of elements to search</param>
        public int IndexOf(T item, int index, int count)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.IndexOf(item, index, count);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Inserts the item into the list
        /// </summary>
        /// <param name="index">the index at which to insert the item</param>
        /// <param name="item">the item to insert</param>
        public void Insert(int index, T item)
        {
            LockList.ExitWriteLock();
            try
            {
                m_TList.Insert(index, item);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Insert a range of objects into the list
        /// </summary>
        /// <param name="index">index to insert at</param>
        /// <param name="range">range of values to insert</param>
        public void InsertRange(int index, IEnumerable<T> range)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.InsertRange(index, range);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>
        /// Returns the last index of the item in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        public int LastIndexOf(T item)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.LastIndexOf(item);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the last index of the item in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        /// <param name="index">the index at which to start searching</param>
        public int LastIndexOf(T item, int index)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.LastIndexOf(item, index);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns the last index of the item in the list
        /// </summary>
        /// <param name="item">the item to find</param>
        /// <param name="index">the index at which to start searching</param>
        /// <param name="count">number of elements to search</param>
        public int LastIndexOf(T item, int index, int count)
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.LastIndexOf(item, index, count);
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Removes this item from the list
        /// </summary>
        /// <param name="item">the item to remove</param>
        public bool Remove(T item)
        {
            LockList.EnterWriteLock();
            try
            {
                return m_TList.Remove(item);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// My Special one
        /// </summary>
        /// <param name="item">the item to remove</param>
        /// <returns></returns>
        public bool RemoveSafely(T item)
        {
            bool Result = false;
            LockList.EnterWriteLock();
            try
            {
                int ndx = m_TList.IndexOf(item);
                if (ndx != -1)
                {
                    m_TList.RemoveAt(ndx);
                    Result = true;
                }
            }
            finally
            {
                LockList.ExitWriteLock();
            }
            return Result;
        }

        /// <summary>
        /// Removes all the matching items from the list
        /// </summary>
        /// <param name="match">the pattern to search on</param>
        public int RemoveAll(Predicate<T> match)
        {
            LockList.EnterWriteLock();
            try
            {
                return m_TList.RemoveAll(match);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes the item at the specified index
        /// </summary>
        /// <param name="index">the index of the item to remove</param>
        public void RemoveAt(int index)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.RemoveAt(index);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes the items from the list
        /// </summary>
        /// <param name="index">the index of the item to begin removing</param>
        /// <param name="count">the number of items to remove</param>
        public void RemoveRange(int index, int count)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.RemoveRange(index, count);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Reverses the order of elements in the list
        /// </summary>
        public void Reverse()
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Reverse();
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Reverses the order of elements in the list
        /// </summary>
        /// <param name="index">the index to begin reversing at</param>
        /// <param name="count">the number of elements to reverse</param>
        public void Reverse(int index, int count)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Reverse(index, count);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sorts the items in the list
        /// </summary>
        public void Sort()
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Sort();
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sorts the items in the list
        /// </summary>
        /// <param name="comparison">the comparison to use when comparing elements</param>
        public void Sort(Comparison<T> comparison)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Sort(comparison);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sorts the items in the list
        /// </summary>
        /// <param name="comparer">the comparer to use when comparing elements</param>
        public void Sort(IComparer<T> comparer)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Sort(comparer);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sorts the items in the list
        /// </summary>
        /// <param name="index">the index to begin sorting at</param>
        /// <param name="count">the number of elements to sort</param>
        /// <param name="comparer">the comparer to use when sorting</param>
        public void Sort(int index, int count, IComparer<T> comparer)
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.Sort(index, count, comparer);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Copies the elements of the list to an array
        /// </summary>
        public T[] ToArray()
        {
            LockList.EnterReadLock();
            try
            {
                return m_TList.ToArray();
            }
            finally
            {
                LockList.ExitReadLock();
            }
        }

        /// <summary>
        /// Sets the capacity to the actual number of elements in the list, if that 
        /// number is less than the threshold
        /// </summary>
        public void TrimExcess()
        {
            LockList.EnterWriteLock();
            try
            {
                m_TList.TrimExcess();
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }

        /// <summary>
        /// Determines whether all members of the list matches the conditions in the predicate
        /// </summary>
        /// <param name="match">the delegate which defines the conditions for the search</param>
        public bool TrueForAll(Predicate<T> match)
        {
            LockList.EnterWriteLock();
            try
            {
                return m_TList.TrueForAll(match);
            }
            finally
            {
                LockList.ExitWriteLock();
            }
        }
    }
}
