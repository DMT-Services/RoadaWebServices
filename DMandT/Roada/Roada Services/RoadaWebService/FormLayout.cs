using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DMTServices
{
    public class FormLayout
    {
        public string name { get; set; }
        public int deficiencyId { get; set; }
        public int displayOrder { get; set; }
        public int problemSid { get; set; }
        public string notes { get; set; }
        public string toolTip { get; set; }
    }

    public class FormHelper
    {
        public List<FormLayout> FormsLayout { get; set; }

        public List<FormLayout> ReadList(string inFile)
        {
            List<FormLayout> flist = new List<FormLayout>();
            // read JSON directly from a file
            using (StreamReader file = File.OpenText(inFile))
            using (JsonTextReader reader = new JsonTextReader(file))
            {
                JObject o2 = (JObject)JToken.ReadFrom(reader);
                JArray a = (JArray)o2["FormLayout"];

                flist = a.ToObject<List<FormLayout>>();
            }
            return flist;
        }

        public int Import(SqlStmt stmt, string inFile, out string errMess)
        {
            int cnt = -1;
            errMess = string.Empty;
            try
            {
                List<FormLayout> flay = ReadList(inFile);
                stmt.DoSQL("DELETE FROM FormsLayout");
                SqlHelper sql = new SqlHelper(SqlHelper.dbType.SqlServer);
                foreach (FormLayout f in flay)
                {
                    sql.Clear();
                    sql.AddNow("idate");
                    sql.Add("name", f.name);
                    sql.Add("deficiencyid", f.deficiencyId);
                    sql.Add("displayOrder", f.displayOrder);
                    sql.Add("problemSid", f.problemSid);
                    sql.Add("notes", f.notes);
                    sql.Add("tooltip", f.toolTip);
                    stmt.DoSQL(sql.BuildInsert("INSERT INTO FormsLayout"));
                    if (stmt.LastError != string.Empty)
                    {
                        errMess = string.Format("Error inserting {0}", stmt.LastError);
                        break;
                    }
                }
                cnt = flay.Count;
            }
            catch (Exception ex)
            {
                errMess = string.Format("ImportJson Excp: {0}", ex.Message);

            }
            return cnt;
        }


        public string Export(SqlStmt stmt, out string errMess)
        {
            errMess = string.Empty;
            string jsonStr = string.Empty;
            try
            {
                bool rc = stmt.DoSQL("SELECT * FROM FormsLayout ORDER BY deficiencyid");
                if (stmt.LastError != string.Empty)
                {
                    errMess = string.Format("BuildJson() Error trying to read FormsLayout table: {0}", stmt.LastError);
                }
                else
                {
                    // wrap it up with a FormContainer class
                    this.FormsLayout = new List<FormLayout>();
                    while (rc)
                    {
                        FormLayout f = new FormLayout();
                        f.name = stmt.fetch("name");
                        f.deficiencyId = stmt.ifetch("deficiencyid");
                        f.displayOrder = stmt.ifetch("displayOrder");
                        f.problemSid = stmt.ifetch("problemSid");
                        f.notes = stmt.fetch("notes");
                        f.toolTip = stmt.fetch("tooltip");
                        this.FormsLayout.Add(f);
                        rc = stmt.dbSkip();
                    }
                    jsonStr = JsonConvert.SerializeObject(this, Formatting.Indented);
                }
            }
            catch (Exception ex)
            {
                errMess = string.Format("BuildJson() Excp: {0}", ex.Message);
            }
            return jsonStr;
        }

    }


}
