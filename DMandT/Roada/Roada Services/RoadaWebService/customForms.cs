using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using Newtonsoft.Json;

namespace DMTServices
{
    /// <summary>
    /// Class for using json text file to control Deficiency data entry/storage
    /// </summary>
    public static class customForms
    {
        public static aForm DefForm = null;

        [DataContract]
        public class form_item
        {
            [DataMember]
            public string prompt;
            [DataMember]
            public string field;
            [DataMember]
            public string type;
            [DataMember]
            public string items;
            [DataMember]
            public int width;
            [DataMember]
            public int height;
            [DataMember]
            public bool multiSelect;
            [DataMember]
            public bool manditory;
            [DataMember]
            public bool vertical;
            [DataMember]
            public bool readOnly;
            [DataMember]
            public string @default { get; set; }
        }

        [DataContract]
        public class form
        {
            [DataMember]
            public string name;
            [DataMember]
            public int deficiencyId;
            [DataMember]
            public int displayOrder;
            [DataMember]
            public int problemSid;
            [DataMember]
            public string problemCat;
            [DataMember]
            public string problemCode;
            [DataMember]
            public string tooltip;
            [DataMember]
            public string notes;
            [DataMember]
            public form_item[] form_items;
        }

        [DataContract]
        public class aForm
        {
            [DataMember]
            public form[] forms;

            public string errorMsg;
        }

        public static T DeserializeFromStream<T>(Stream stream)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();

            using (var sr = new StreamReader(stream))
            {
                return serializer.Deserialize<T>(sr.ReadToEnd());
            }
        }

        public static aForm GetJsonLayoutFile(string filename)
        {
            DefForm = null;
            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
                    DefForm = DeserializeFromStream<aForm>(fs);
            }
            catch (Exception ex)
            {
                if (DefForm == null)
                    DefForm = new aForm();
                DefForm.errorMsg = ex.Message;
            }

            return DefForm;
        }

        public static aForm GetSQLLayoutFile(string ConnString)
        {
            if (DefForm == null || DefForm.forms.Length == 0)
            {
                SqlStmt stmt2 = null;
                SqlStmt stmt = new SqlStmt(ConnString);
                try
                {
                    stmt2 = new SqlStmt(ConnString);
                    GetSQLLayoutFile(stmt, stmt2);
                }
                finally
                {
                    stmt.Close();
                    stmt = null;
                    if (stmt2 != null)
                    {
                        stmt2.Close();
                        stmt2 = null;
                    }
                }
            }
            return DefForm;
        }

        public static aForm GetSQLLayoutFile(SqlStmt stmt, SqlStmt stmt2)
        {
            DefForm = new aForm();
            try
            {
                List<form> formList = new List<form>();
                bool rc = stmt.DoSQL("SELECT * FROM formsLayout ORDER BY id");
                while (rc)
                {
                    customForms.form f = new customForms.form();
                    f.name = stmt.fetch("name");
                    f.displayOrder = stmt.ifetch("displayOrder");
                    f.deficiencyId = stmt.ifetch("deficiencyId");
                    f.problemSid = stmt.ifetch("problemsid");
                    f.problemCat = stmt.fetch("problemcat");
                    f.problemCode = stmt.fetch("problemcode");
                    f.tooltip = stmt.fetch("tooltip");
                    f.notes = stmt.fetch("notes");
                    List<customForms.form_item> itemList = new List<customForms.form_item>();
                    bool rc2 = stmt2.DoSQLFormat("SELECT * FROM formItems WHERE layout_id = {0} ORDER BY id", stmt.ifetch("id"));
                    while (rc2)
                    {
                        customForms.form_item fi = new customForms.form_item();
                        fi.prompt = stmt2.fetch("prompt");
                        fi.field = stmt2.fetch("field");
                        fi.type = stmt2.fetch("ftype");
                        fi.width = stmt2.ifetch("width");
                        fi.multiSelect = stmt2.tfetch("multiselect");
                        fi.vertical = stmt2.tfetch("vertical");
                        fi.readOnly = stmt2.tfetch("readonly");
                        itemList.Add(fi);

                        rc2 = stmt2.dbSkip();
                    }
                    f.form_items = itemList.ToArray();
                    itemList.Clear();
                    formList.Add(f);

                    rc = stmt.dbSkip();
                }
                DefForm.forms = formList.ToArray();
                formList.Clear();
                DefForm.errorMsg = string.Empty;
            }
            catch (Exception ex)
            {
                if (DefForm == null)
                    DefForm = new aForm();
                DefForm.errorMsg = ex.Message;
            }

            return DefForm;
        }

        public static string typeToName(int type)
        {
            return typeToName(type, DefForm);
        }

        public static string typeToName(int type, aForm dForm)
        {
            string rc = string.Empty;
            form frm = dForm.forms.FirstOrDefault(x => x.deficiencyId == type);
            if (frm != null)
                rc = frm.name;
            return rc;
        }

        public static string typeName(aForm layout, int defType, out string category)
        {
            form frm = layout.forms.FirstOrDefault(x => x.deficiencyId == defType);
            category = (frm != null) ? frm.problemCat : string.Empty;
            return (frm != null) ? frm.name : string.Empty;
        }

        public static string typeName(aForm layout, int defType)
        {
            string cat;
            return typeName(layout, defType, out cat);
        }

        public static form GetDetails(SqlStmt row, int defId, bool ex, out string text)
        {
            return GetDetails(row, defId, ex, out text, false);
        }

        public static form FindFormByProblemSID(int problemSid)
        {
            return Array.Find<customForms.form>(customForms.DefForm.forms, x => x.problemSid == problemSid);
        }

        public static form GetDetails(SqlStmt row, int defId, bool ex, out string text, bool noCr)
        {
            text = string.Empty;
            customForms.form frm = Array.Find<customForms.form>(customForms.DefForm.forms, x => x.deficiencyId == defId);
            if (frm == null)
            {
                frm = new customForms.form();
                frm.form_items = new customForms.form_item[0];
            }
            foreach (customForms.form_item itm in frm.form_items)
            {
                string sval = string.Empty;
                int ival = 0;

                if (!itm.field.Contains("|"))
                    switch (itm.field)
                    {
                        case "item":
                        case "issue":
                        case "status":
                        case "length":
                        case "width":
                        case "depth":
                            ival = row.ifetch(itm.field);
                            break;
                        case "option":
                            ival = row.ifetch("opt");
                            break;
                        case "note":
                            sval = row.fetch("notes");
                            break;
                    }
                else
                {
                    int val = 0;
                    if (itm.field.StartsWith("option"))
                        val = row.ifetch("opt");
                    if (val > 0)
                    {
                        string[] fs = itm.field.Split('|');
                        if (fs.Length > 1)
                        {
                            int ndx = myConvert.ToInt(fs[1], 0);
                            if (ndx > 0)
                                // reverse of:	((int)Math.Pow(10, ndx - 1)) * val;
                                ival = val % ((int)Math.Pow(10, ndx)) / ((int)Math.Pow(10, ndx - 1));
                        }
                    }
                }

                switch (itm.type)
                {
                    case "textbox":
                        text += itm.prompt + (ex ? ":  " : noCr ? "|" : ":\r\n") + sval + (noCr ? "|" : "\r\n");
                        break;
                    case "numericbox":
                        text += itm.prompt + ":  " + ival.ToString() + (noCr ? "|" : "\r\n");
                        break;
                    case "dropdown":
                        if (ival > 0)
                        {
                            string[] list = itm.items.Split(';');

                            if (ival - 1 < list.Length)
                            {
                                if (ex)
                                    text += itm.prompt.Replace("\"", "'") + ":  " + list[ival - 1].Replace("\"", "'") + (noCr ? "|" : "\r\n");
                                else
                                    text += itm.prompt + ":  " + list[ival - 1] + (noCr ? "|" : "\r\n");
                            }
                        }
                        break;
                    case "listbox":
                        if (ival > 0)
                        {
                            string[] list = itm.items.Split(';');

                            if (!itm.multiSelect)
                            {
                                if (ival - 1 < list.Length)
                                    text += itm.prompt + ":  " + list[ival - 1] + (noCr ? "|" : "\r\n");
                            }
                            else
                            {
                                string sub = string.Empty;

                                // reverse of:	if (checked) ival += (int)Math.Pow(2, ndx - 1);
                                for (int ndx = 0; ndx < list.Length; ndx++)
                                    if (ival % ((int)Math.Pow(2, ndx + 1)) / ((int)Math.Pow(2, ndx)) == 1)
                                        sub += list[ndx] + "; ";

                                if (sub != string.Empty)
                                    text += itm.prompt + ":  " + sub.Substring(0, sub.Length - 2) + (noCr ? ".|" : ".\r\n");
                            }
                        }
                        break;
                    case "yesno":
                        if (ival == 1)                  // we only care about/show YES (not NO = 0 or N/A = 2).
                            text += itm.prompt + "  YES" + (noCr ? "|" : "\r\n");
                        break;
                    case "readonlybox":                 // should be custom field ... should i show these?
                        break;
                }
            }
            return frm;
        }
#if CRAP
        /// <summary>
        /// returns a string array of all the Deficiency names in order by id
        /// </summary>
        /// <param name="layout"></param>
        /// <returns>string[]</returns>
        public static string[] getCodes(aForm layout)
        {
            List<string> types = new List<string>();

            // this isn't the best way to do this but it allowed adding the change without any other code mods...
            bool none = true;
            int bNo = 0;
            types.Add(string.Empty);                    // index 0 is ALWAYS empty.
            while (++bNo < 50)
            {
                form frm = layout.forms.FirstOrDefault(x => x.deficiencyId == bNo);
                if (frm != null)
                {
                    types.Add(frm.name);
                    none = false;
                }
                else
                    types.Add(string.Empty);
            }

            // to account for older files that didn't have the deficiencyId field and just used the physical order to determine the id
            if (none)
            {
                types.Clear();
                types.Add(string.Empty);                    // index 0 is ALWAYS empty.
                foreach (form type in layout.forms)
                    types.Add(type.name);
            }

            return types.ToArray();
        }
#endif
    }
}
#if CRAP
    
Deficiencies 
> Signs 
•	List box with type of sign from GIS information.
Checkerboard,	Curve sign with advisory speed,	Do not enter,
Load Restricted Bridge,	Low Bridge,	Low Bridge Ahead,
One Way,	School Zone Speed Limit,	Stop,
Stop Ahead,	Stop Ahead New,	Traffic Signal Ahead New,
Two Way Traffic Ahead,	Wrong Way,	Yield,
Yield Ahead,	Yield Ahead New.
•	Status
o	Damaged Sign
o	Missing Sign
•	Comments

> Potholes
•	Length of pothole in cm. 
o	Used in calculating the surface area. 
o	Could range from 1cm to 100cm.
•	Width of pothole in cm. 
o	Used in calculating the surface area. 
o	Could range from 1cm to 100cm.
•	Surface area of pothole in cm2.
o	Calculated from length and width.
•	Depth of pothole in cm. 
o	Could range from 1cm to 30cm.
•	Type of road.
o	Paved (Default to this option).
o	Shoulder (Paved or Non-Paved).
o	Non-paved.
•	Picture
•	Description of pothole.
If the surface area and depth exceeds the amount in the appropriate table the road must be repaired within the allotted time. 

The tables below shows the minimum maintenance standards for potholes this information is stored in a database for reference
	Paved Road Surface
	Class of Highway	Surface Area	Depth	Time
	1	600 cm²	8 cm	4 days
	2	800 cm²	8 cm	4 days
	3	1000 cm²	8 cm	7 days
	4	1000 cm²	8 cm	14 days
	5	1000 cm²	8 cm	30 days

	Non-Paved Road
	Class of Highway	Surface Area	Depth	Time
	3	1500 cm²	8 cm	7 days
	4	1500 cm²	10 cm	14 days
	5	1500 cm²	12 cm	30 days

	Paved or Non-Paved Shoulder
	Class of Highway	Surface Area	Depth	Time
	1	1500 cm²	8 cm	7 days
	2	1500 cm²	8 cm	7 days
	3	1500 cm²	8 cm	14 days
	4	1500 cm²	10 cm	30 days
	5	1500 cm²	12 cm	60 days

> Sidewalks
•	Deficiency List (List box)
o	Lip Grater than 20mm
o	Protrusion Grater than 20mm
o	Ponding/Reverse Grade/Heaving
o	Wide Crack
o	Multiple Cracks
o	Spalling
o	Concrete Plug Missing
•	Picture
•	Description of sidewalk deficiency

> Traffic Lights
•	Types of deficiencies for traffic lights (allow to select more than 1)
o	1 or more Displays showing conflicting indications 
o	The angle of a traffic control system has been changed
o	A phase of the traffic flow fails to occur 
o	Phase timing issues interfering with the ability to pass through the intersection safely
o	Power Failure
o	The traffic control signal system cabinet has been displaced from its proper position.
o	There is a failure of any of the traffic control signal support structures.
o	A signal lamp or a pedestrian control indication is not functioning.
o	Signals are flashing when flashing mode is not a part of the normal signal operation.
•	Picture
•	Description of deficiency 

> Cracks
•	Enter the pavement crack width in cm
•	Enter the pavement crack depth in cm
•	Enter the pavement crack length in cm
o	If the crack has a length of over 300 cm and the width and depth is greater than 5cm the crack must be repaired within the time specified in the MMS
	Class of Highway	Width	Depth	Time
	1	5 cm	5 cm	30 days
	2	5 cm	5 cm	30 days
	3	5 cm	5 cm	60 days
	4	5 cm	5 cm	180 days
	5	5 cm	5 cm	180 days

> Curb
•	List of Deficiencies 
o	Broken
o	Missing
o	Cracked
•	Picture
•	Comments
> Graffiti
•	Is there Graffiti?
•	Is the graffiti offensive?
•	Picture
•	Comments
> Ditches 
•	Picture
•	Comments
> Flooding
•	Is there flooding on the road?
•	Is the flooding effecting the flow of traffic?
> Catch Basin
•	Is the catch Basin overflowing?
•	Is the catch basin Damaged?
o	Broken
o	Missing
o	Cracked
•	Pictures
•	Comments
> Shoulder 
•	Enter the should depth in cm
o	If the should depth is greater than 8cm repair needs to happen within time specified in MMS dependant on the road class.
•	Pictures
•	Comments
	Class of Highway	Depth	Time
	1	8 cm	4 days
	2	8 cm	4 days
	3	8 cm	7 days
	4	8 cm	14 days
	5	8 cm	30 days

> Boulevard 
•	Is the sod damaged?
•	Pictures 
•	Comments

> Pavement markings
•	Are the pavement markings visible?
•	Pictures
•	Comments 

> Manholes
•	Does the manhole sit flush with the pavement?
•	Is the manhole damaged?
o	Broken
o	Cracked
o	Missing
•	Pictures
•	Comments
> Fences
•	How is the fence damaged?
o	Hole
o	Knocked Over
o	Missing panel
•	Pictures
•	Comments

> Guide rails
•	How is the guide rail damaged?
o	Cracked
o	Missing
o	Broken
•	Pictures
•	Comments

> Tree blocking sign
•	Are there tree branches blocking a street sign?
•	Picture 
•	Comment

> Culverts
•	Is the culvert damaged?
•	Is the culvert clogged with leaves?
•	Is the culvert corroded?
•	Has the damage to the culvert damaged the pavement?

> Garbage/Debris
•	Is there garbage on the road?
•	Is it blocking traffic?
•	Pictures
•	Comments
> Dirty roads
•	What dirt is on the road
o	Mud
o	Oil spill
o	Gravel 
o	Other
•	Pictures
•	Comments

> Entrance Features 
•	How heavy is the damage?
o	Light
o	Moderate
o	Heavy
•	Pictures
•	Comments

> Line of Sight issues
•	Is there something blocking line of sight at an intersection?
•	Pictures
•	Comments
#endif
