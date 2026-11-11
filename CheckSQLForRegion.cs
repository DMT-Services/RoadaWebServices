using System;
using System.Collections.Generic;

using DMTServices;

/* Class is used to estimate if a passed is relavent or not in the SQL passed.  Needs lots more testing ...
   Moved to its own class for release by KRS 9/4/2012. */

namespace RoadaWebService
{
    public class CheckSQLForRegion
    {

        /*  This does an attempted check to see if a region the data to be replicated can be 'easily'
            established, and if so, disqualify data (set to NULL) that does NOT belong to the "region" we want.
            There is better ways to do this, but many require changes through-out entire project, so this was
            an attempted quickie, added by KRS 8/27/2012. */

        private List<int> Hands = null;
        private List<string> Units = null;
        private List<int> Issues = null;

        public bool PreloadRegion( SqlStmt stmt, string region )
        {
            if (Hands != null)
                Hands.Clear();
            if (Units != null)
                Units.Clear();
            if (Issues != null)
                Issues.Clear();

            bool ok;
            string pre = "";

            try
            {
                ok = stmt.DoSQL("SELECT COUNT(*) AS amount FROM yard");

                if (ok)
                    ok = (stmt.ifetch("amount") > 0);

                if (!ok)
                {
                    // Sometimes patrol information is stored in a separate database from region data ...

                    /* Note:  This avoids adding a second field for database connection string to the "client"
                              table of the master database, although we will want to do that in near future. */
                    string db = "";

                    ok = stmt.DoSQL("SELECT DB_NAME() AS name");

                    if (ok)
                    {
                        db = stmt.fetch("name").Trim().ToLower();
                        ok = false;
                    }

                    if (db.Length > 0)
                        if (db.EndsWith("_patrol"))
                        {
                            pre = "[" + db.Substring(0, db.Length - 7) + "].[dbo].";

                            ok = stmt.DoSQL("SELECT COUNT(*) AS amount FROM " + pre + "yard");

                            if (ok)
                                ok = (stmt.ifetch("amount") > 0);
                        }

                    if (!ok)
                        return false;
                }

                string sql = "SELECT hand_code, unit_num " +
                             "FROM {0}vehicles v " +
                                  "JOIN {0}fleet f ON v.fleet = f.id " +
                                  "JOIN {0}distribution_center y ON f.distribution_id = y.id " +
                                  "JOIN {0}yard a ON y.yard_no = a.yard_no " +
                             "WHERE a.name = '{1}'";

                Hands = new List<int>();
                Units = new List<string>();
                Issues = new List<int>();

                ok = stmt.DoSQL(string.Format(sql, pre, region));

                while (ok)
                {
                    Hands.Add(stmt.ifetch("hand_code"));
                    Units.Add(stmt.fetch("unit_num"));

                    ok = stmt.dbSkip();
                }

                sql = "SELECT issue_id FROM incidents WHERE hand_code IN (hhh) OR vehicle IN (uuu)";

                string sub = "";
                for (int ndx = 0; ndx < Hands.Count; ndx++)
                    sub += Hands[ndx].ToString() + ",";
                sql = sql.Replace("hhh", SubString(sub, 0, sub.Length - 1));

                sub = "";
                for (int ndx = 0; ndx < Units.Count; ndx++)
                {
                    sub += "'" + Units[ndx].ToString() + "',";

                    // Everything is lowercase after here in this quickie (spaces are converted too) ...
                    Units[ndx] = Units[ndx].ToLower().Replace(" ", "\x02");
                }
                sql = sql.Replace("uuu", SubString(sub, 0, sub.Length - 1));

                ok = stmt.DoSQL(sql);

                while (ok)
                {
                    Issues.Add(stmt.ifetch("issue_id"));

                    ok = stmt.dbSkip();
                }
            }
            finally
            {
                stmt.Clear();
            }

            return true;
        }


        // Only pass 1 character strings here ...
        private bool IsNumeric( string key )
        {
            if (key.Length != 1)
                return false;
            else
                return (key == "." || (key[0] >= '0' && key[0] <= '9'));
        }
        private bool IsLetter( string key )
        {
            if (key.Length != 1)
                return false;
            else
                return (key == "_" || key == "~" || (key[0] >= 'a' && key[0] <= 'z'));
        }


        /* Convert SQL into something friendlier for our quick-mini-parse.  Remember that everything (including
           values) is sent to lowercase, and spaces (' ' or '\x20') in strings are converted to '\x02'. */
        private string FormatSQL( string sql, SQLParameter[] par )
        {
            string fmt = "";

            sql = sql.Trim().ToLower();

            int num = -1;
            string cur = "", pre, end = "";

            int len = sql.Length;
            const int max = 50;         // Note:  Long SQL (including values) can take forever to process ... so we protect against that with this.

            for (int ndx = 0; ndx < len; ndx++)
            {
                pre = cur;
                cur = sql.Substring(ndx, 1);

                if (num > -1)
                {
                    // We quote pure numeric values, for easier comparisions later.
                    if (!IsNumeric(cur))
                    {
                        if (!IsLetter(cur))
                        {
                            fmt = fmt.Insert(num, " '");
                            cur = "' " + cur;
                        }
                        num = -1;
                    }

                    fmt += cur;
                }
                else
                {
                    if (end == "")
                    {
                        if (len - ndx > max && cur == "0")
                            if (sql.Substring(ndx, 2) == "0x")
                            {                                                   // Protect against large values ...
                                int next = sql.IndexOf(' ', ndx);
                                int temp = sql.IndexOf(',', ndx);

                                next = (next == -1 ? temp : Math.Min(next, temp));

                                bool last = (next == -1);

                                if (last)
                                    next = len - 5;                             // ... can still be valid? ...

                                if (next - ndx > max)
                                {
                                    fmt += sql.Substring(ndx, max) + "...";

                                    if (!last)
                                        ndx = next - 1;
                                    else
                                        ndx = next - 5;

                                    continue;
                                }
                            }

                        if (IsNumeric(cur) && !IsLetter(pre))
                        {
                            num = fmt.Length;
                        }
                        // For word splitting during parse ...
                        else if (cur == "(" || cur == ")" || cur == ",")
                            cur = " " + cur + " ";
                        else if (cur == "\t" || cur == "\n" || cur == "\r")
                            cur = " ";
                        // For turning on various quote modes (we currently do not handle nested [] brackets) ...
                        else if (cur == "\"" || cur == "\'")
                            end = cur;
                        else if (cur == "[")
                            end = "]";

                        if (end != "" && len - ndx > max)
                        {                                                       // Protect against large values ...
                            int next = sql.IndexOf(end, ndx);

                            if (next == -1)
                                next = len - 1;

                            if (next - ndx > max)
                            {
                                fmt += sql.Substring(ndx, max).Replace(" ", "\x02") + "...";

                                ndx = next - 1;

                                continue;
                            }
                        }

                        fmt += cur;
                    }
                    else
                    {
                        fmt += (cur != " " ? cur : "\x02");

                        if (cur == end)
                            end = "";
                    }
                }
            }

            if (par != null)
                for (int ndx = 0; ndx < par.Length; ndx++)
                {
                    string token = par[ndx].Key.Trim().ToLower();

                    if (!token.StartsWith("@"))
                        token = "@" + token;

                    string value = myConvert.ToString(par[ndx].Value).ToLower();

                    value = value.Replace(" ", "\x02");

                    if (value.Length > max)                                     // Large values are irrelavent here ...
                        value = value.Substring(0, max) + "...";

                    fmt = fmt.Replace("'" + token + "'", token);                // Unquote because we requote all (is ever this necessary?).
                    fmt = fmt.Replace(token, "'" + value + "'");
                }

            return fmt;
        }


        // This eliminates exception risk ...
        private string SubString( string value, int start, int length )
        {
            int len = value.Length;
            if (start >= len)
                return "";
            else if (start + length > len)
                length = len - start;
            return value.Substring(start, length);
        }


        private int Count( string value, char key )
        {
            int count = 0;

            for (int ndx = 0; ndx < value.Length; ndx++)
                if (value[ndx] == key)
                    count++;

            return count;
        }


        // This only returns where the equation is an exact equality (only works for forward equations) ...
        private bool ExractValue( string equation, out string field, out bool equal, out string value )
        {
            field = "";
            equal = false;
            value = "";

            for (int ndx = 0; ndx < equation.Length; ndx++)
            {
                string bit = SubString(equation, ndx, 4);

                if (bit == " in " || bit == " in(")
                {
                    field = equation.Substring(0, ndx).Trim();
                    equal = false;
                    value = equation.Substring(ndx + 3).Trim();

                    return true;
                }

                bit = SubString(bit, 0, 2);

                if (bit == ">=" || bit == "<=" || bit == "<>")
                    return false;

                bit = SubString(bit, 0, 1);

                if (bit == ">" || bit == "<")
                    return false;

                if (bit == "=")
                {
                    field = equation.Substring(0, ndx).Trim();
                    equal = true;
                    value = equation.Substring(ndx + 1).Trim();

                    return true;
                }
            }

            return false;
        }

        // This only returns a check-able value or nothing ...
        private string CheckValue( string value )
        {
            value = value.Trim();

            int len = value.Length;

            if (len > 4)
                while (value[0] == '(' && value[len - 1] == ')')
                {
                    value = value.Substring(1, len - 2).Trim();

                    len = value.Length;

                    if (len <= 4)
                        break;
                }

            if (len > 2)
            {
                if (value[0] == '\'' && value[len - 1] == '\'')
                {
                    value = value.Substring(1, len - 2);

                    if (value.IndexOf('\'') > -1)
                        value = "";
                }
                else
                    value = "";
            }
            else
                value = "";

            return value;
        }


        private enum BoolPlus { None = 0, False = 1, True = 2 };


        private BoolPlus InRegion( string table, string field, bool equal, string value )
        {
            BoolPlus result = BoolPlus.None;

            int mode = 0;

            if (table.Contains("messagein"))
            {
                if (field.Contains("hand_code"))
                    mode = 1;
                else if (field.Contains("unit_num"))
                    mode = 2;
            }
            else if (table.Contains("incident"))
            {
                if (field.Contains("issue_id"))
                    mode = 3;
                else if (field.Contains("hand_code"))
                    mode = 1;
                else if (field.Contains("vehicle"))
                    mode = 2;
            }

            if (mode > 0)
            {
                string[] values = null;

                if (equal)
                {
                    values = new string[1];
                    values[0] = value;
                }
                else
                {
                    int len = value.Length;

                    if (len > 2)
                        if (value[0] == '(' && value[len - 1] == ')')
                            value = value.Substring(1, len - 2).Trim();

                    values = value.Split(',');
                }

                for (int ndx = 0; ndx < values.Length; ndx++)
                {
                    values[ndx] = CheckValue(values[ndx]);

                    if (values[ndx].Length > 0)
                        switch (mode)
                        {
                            case 1:
                                if (Hands.Contains(myConvert.StrToIntDef(values[ndx], 0)))
                                    result = BoolPlus.True;
                                else
                                    result = BoolPlus.False;
                                break;
                            case 2:
                                if (Units.Contains(values[ndx]))
                                    result = BoolPlus.True;
                                else
                                    result = BoolPlus.False;
                                break;
                            case 3:
                                if (Issues.Contains(myConvert.StrToIntDef(values[ndx], 0)))
                                    result = BoolPlus.True;
                                else
                                    result = BoolPlus.False;
                                break;
                        }
                }
            }

            return result;
        }

        private string[] SplitWhere( string where )
        {
            List<string> args = new List<string>();

            if (!where.Contains(" or "))
            {
                where = where.Replace(" not null ", " not-null ");

                if (!where.Contains(" not "))
                {
                    string[] and = new string[] { " and " };

                    args.AddRange(new List<string>(where.Split(and, StringSplitOptions.RemoveEmptyEntries)));

                    for (int ndx = 0; ndx < args.Count; ndx++)
                    {                   // Strip any ( / ) that span arguments ...
                        if (args[ndx].StartsWith("(") || args[ndx].EndsWith(")"))
                        {
                            int bs = Count(args[ndx], '(');
                            int bf = Count(args[ndx], ')');

                            while (args[ndx].StartsWith("("))
                                if (bs > bf)
                                {
                                    args[ndx] = args[ndx].Substring(1).Trim();
                                    bs--;
                                }
                                else
                                    break;

                            while (args[ndx].EndsWith(")"))
                                if (bs < bf)
                                {
                                    args[ndx] = args[ndx].Substring(0, args[ndx].Length - 1).Trim();
                                    bf--;
                                }
                                else
                                    break;
                        }
                    }
                }
            }

            return args.ToArray();
        }


        public bool CheckRegion( string region, ref ReplicationData data )
        {
            bool sync = CheckRegion(region, data);

            if (!sync)
                data = null;

            return sync;
        }

        /* Returns true should this SQL command need to be synced; such as it belonging to this region,
           or a structure change, or undetermined. */
        private bool CheckRegion( string region, ReplicationData data )
        {
            string sql = FormatSQL(data.SqlStr, data.Params);

            char[] space = new char[] { ' ' };

            string[] words = sql.Split(space, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length >= 3)
            {
                string type = words[0];

                List<string> tables = new List<string>();
                List<string> fields = new List<string>();
                List<string> values = new List<string>();
                string symbol = "", select = "", from = "", where = "";

                int ndx = 1;
                int mode = 0;
                int nest = 0;

                if (type == "insert")                       // INSERT mini-parse ...
                {
                    if (words[ndx] == "into")
                        ndx++;

                    for (int wdx = ndx; wdx < words.Length; wdx++)
                    {
                        string word = words[wdx];

                        if (mode == 0)
                        {               // tables
                            if (word == "(")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                                mode++;
                            }
                            else if (word == ",")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 1 || mode == 6)
                        {               // fields + VALUES
                            if (word == "(")
                            {
                                symbol += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                if (nest == 0)
                                {
                                    if (symbol != "")
                                    {
                                        if (mode == 1)
                                            fields.Add(symbol);
                                        else
                                            values.Add(symbol);
                                    }
                                    symbol = "";
                                    mode++;
                                }
                                else
                                {
                                    symbol += word + " ";
                                    nest--;
                                }
                            }
                            else if (word == "," && nest == 0)
                            {
                                if (symbol != "")
                                {
                                    if (mode == 1)
                                        fields.Add(symbol);
                                    else
                                        values.Add(symbol);
                                }
                                symbol = "";
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 2)
                        {
                            if (word == "values")
                            {
                                if (wdx + 1 < words.Length)
                                    if (words[wdx + 1] == "(")
                                        wdx++;              // ... because of shared logic (for mode 6), we need to set this up as in the case of mode 1.
                                mode += 4;
                            }
                            else if (word != "(")
                                mode++;
                        }
                        else if (mode == 3 || mode == 4)
                        {               // SELECT + FROM
                            if (word == "(")
                            {
                                symbol += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                if (nest == 0)
                                    break;
                                else
                                {
                                    symbol += word + " ";
                                    nest--;
                                }
                            }
                            else if (mode == 3 && word == "from" && nest == 0)
                            {
                                select += symbol;
                                symbol = "";
                                mode++;
                            }
                            else if (mode == 4 && word == "where" && nest == 0)
                            {
                                from += symbol;
                                symbol = "";
                                mode++;
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 5)
                        {               // WHERE
                            if (word == "(")
                            {
                                where += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                if (nest == 0)
                                    break;
                                else
                                {
                                    where += word + " ";
                                    nest--;
                                }
                            }
                            else
                                where += word + " ";
                        }
                        else
                            break;
                    }
                }
                else if (type == "update")                  // UPDATE mini-parse ...
                {
                    for (int wdx = ndx; wdx < words.Length; wdx++)
                    {
                        string word = words[wdx];

                        if (mode == 0)
                        {               // tables
                            if (word == "set")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                                mode++;
                            }
                            else if (word == ",")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 1)
                        {               // SET
                            if (word == "(")
                            {
                                symbol += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                symbol += word + " ";

                                if (nest == 0)
                                    break;
                                else
                                    nest--;
                            }
                            else if ((word == "from" || word == "where") && nest == 0)
                            {
                                if (symbol != "")
                                    fields.Add(symbol);
                                symbol = "";
                                if (word == "from")
                                    mode++;
                                else
                                    mode += 2;
                            }
                            else if (word == "," && nest == 0)
                            {
                                if (symbol != "")
                                    fields.Add(symbol);
                                symbol = "";
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 2)
                        {               // FROM
                            if (word == "(")
                            {
                                from += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                from += word + " ";
                                if (nest == 0)
                                    break;
                                else
                                    nest--;
                            }
                            else if (word == "where" && nest == 0)
                                mode++;
                            else
                                from += word + " ";
                        }
                        else if (mode == 3)
                        {               // WHERE
                            if (word == "(")
                            {
                                where += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                if (nest == 0)
                                    break;
                                else
                                {
                                    where += word + " ";
                                    nest--;
                                }
                            }
                            else
                                where += word + " ";
                        }
                        else
                            break;
                    }
                }
                else if (type == "delete")                  // DELETE mini-parse ...
                {
                    for (int wdx = ndx; wdx < words.Length; wdx++)
                    {
                        string word = words[wdx];

                        if (mode == 0)
                        {               // tables
                            if (word == "where")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                                mode++;
                            }
                            else if (word == "," || word == "from")
                            {
                                if (symbol != "")
                                    tables.Add(symbol);
                                symbol = "";
                            }
                            else
                                symbol += word + " ";
                        }
                        else if (mode == 1)
                        {               // WHERE
                            if (word == "(")
                            {
                                where += word + " ";
                                nest++;
                            }
                            else if (word == ")")
                            {
                                if (nest == 0)
                                    break;
                                else
                                {
                                    where += word + " ";
                                    nest--;
                                }
                            }
                            else
                                where += word + " ";
                        }
                        else
                            break;
                    }
                }
                else
                    return true;

                // --------------------------------------------------------------------------------

                if (type == "insert")                       // INSERT check ...
                {
                    if (tables.Count == 1)
                    {
                        if (tables[0].Contains("messagein") || tables[0].Contains("incident"))
                        {
                            int not = 0;
                            BoolPlus ok;

                            if (values.Count > 0)
                            {
                                for (ndx = 0; ndx < fields.Count; ndx++)
                                {
                                    if (ndx >= values.Count)
                                        break;

                                    ok = InRegion(tables[0], fields[ndx], true, values[ndx]);

                                    switch (ok)
                                    {
                                        case BoolPlus.True:
                                            return true;
                                        case BoolPlus.False:
                                            not++;
                                            break;
                                    }
                                }
                            }
                            else
                            {
                                if (from.Length > 0)
                                    if (from.Contains(" , ") || from.Contains(" join "))
                                        return true;                                // We won't bother checking these.

                                if (where.Length > 0)
                                {
                                    string[] args = SplitWhere(where);

                                    for (ndx = 0; ndx < args.Length; ndx++)
                                    {
                                        string field, value;
                                        bool equal;

                                        if (ExractValue(args[ndx], out field, out equal, out value))
                                        {
                                            ok = InRegion(tables[0], field, equal, value);

                                            switch (ok)
                                            {
                                                case BoolPlus.True:
                                                    return true;
                                                case BoolPlus.False:
                                                    not++;
                                                    break;
                                            }
                                        }
                                    }
                                }
                            }

                            if (not > 0)
                                return false;
                        }
                    }

                    return true;
                }
                else if (type == "update")                  // UPDATE check ...
                {
                    if (tables.Count == 1 && from.Length == 0)
                    {
                        if (tables[0].Contains("messagein") || tables[0].Contains("incident"))
                            if (fields.Count > 0)
                            {
                                int not = 0;
                                BoolPlus ok;

                                string field, value;
                                bool equal;

                                for (ndx = 0; ndx < fields.Count; ndx++)
                                    if (ExractValue(fields[ndx].Trim(), out field, out equal, out value))
                                    {
                                        ok = InRegion(tables[0], field, equal, value);

                                        switch (ok)
                                        {
                                            case BoolPlus.True:
                                                return true;
                                            case BoolPlus.False:
                                                not++;
                                                break;
                                        }
                                    }

                                if (not > 0)
                                    return false;

                                if (where.Length > 0)
                                {
                                    string[] args = SplitWhere(where);

                                    for (ndx = 0; ndx < args.Length; ndx++)
                                        if (ExractValue(args[ndx], out field, out equal, out value))
                                        {
                                            ok = InRegion(tables[0], field, equal, value);

                                            switch (ok)
                                            {
                                                case BoolPlus.True:
                                                    return true;
                                                case BoolPlus.False:
                                                    not++;
                                                    break;
                                            }
                                        }
                                }

                                if (not > 0)
                                    return false;
                            }
                    }

                    return true;
                }
                else if (type == "delete")                  // DELETE check ...
                {
                    if (tables.Count == 1)
                    {
                        if (tables[0].Contains("messagein") || tables[0].Contains("incident"))
                            if (where.Length > 0)
                            {
                                int not = 0;
                                BoolPlus ok;

                                string field, value;
                                bool equal;

                                string[] args = SplitWhere(where);

                                for (ndx = 0; ndx < args.Length; ndx++)
                                    if (ExractValue(args[ndx], out field, out equal, out value))
                                    {
                                        ok = InRegion(tables[0], field, equal, value);

                                        switch (ok)
                                        {
                                            case BoolPlus.True:
                                                return true;
                                            case BoolPlus.False:
                                                not++;
                                                break;
                                        }
                                    }

                                if (not > 0)
                                    return false;
                            }
                    }

                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            if (Hands != null)
                Hands.Clear();
            if (Units != null)
                Units.Clear();
            if (Issues != null)
                Issues.Clear();
        }
    }
}
