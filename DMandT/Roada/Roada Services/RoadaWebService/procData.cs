using System;
using System.ComponentModel;

using DMTServices;

public static class DateTimeJSON
{
    public static readonly DateTime MinValue = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public static class procData
{
    public static void fillQuery(object cl, string[,] layout, SqlHelper query)
    {
        fillQuery(cl, layout, query, false);
    }

    public static void fillQuery(object cl, string[,] layout, SqlHelper query, bool allowBlanks)
    {
        try
        {
            PropertyDescriptorCollection ordersProps = TypeDescriptor.GetProperties(cl);
            for (int i = 0; i < layout.GetLength(0); i++)
            {
                PropertyDescriptor s = ordersProps.Find(layout[i, 0], true);
                if (s != null)
                {
                    object v = s.GetValue(cl);

                    bool add = true;
                    // skip 'empty' data...
                    if (!allowBlanks)
                        switch (s.PropertyType.Name.ToLower())
                        {
                            case "string":
                                add = !IsNullorNothing((string)v);
                                break;
                            case "datetime":
                                add = ((DateTime)v != DateTime.MinValue && (DateTime)v != DateTimeJSON.MinValue);
                                break;
                        }
                    if (add)
                        query.Add(layout[i, 1], v);
                }
            }
        }
        catch
        {
            throw;
        }
    }

    public static void fillClass(object cl, string[,] layout, SqlStmt stmt)
    {
        try
        {
            PropertyDescriptorCollection ordersProps = TypeDescriptor.GetProperties(cl);
            for (int i = 0; i < layout.GetLength(0); i++)
            {
                PropertyDescriptor s = ordersProps.Find(layout[i, 0], true);
                if (s != null)
                {
                    switch (s.PropertyType.Name.ToLower())
                    {
                        case "double":
                            s.SetValue(cl, stmt.dfetch(layout[i, 1]));
                            break;
                        case "datetime":
                            s.SetValue(cl, stmt.dtfetch(layout[i, 1]));
                            break;
                        case "int32":
                            s.SetValue(cl, stmt.ifetch(layout[i, 1]));
                            break;
                        default:
                            object v = stmt.ofetch(layout[i, 1]);
                            if (v != null && !(v is System.DBNull))
                                s.SetValue(cl, v);
                            break;
                    }
                }
            }
        }
        catch
        {
            throw;
        }
    }

    private static bool IsNullorNothing(string chk)
    {
        return chk == null || chk.Trim() == string.Empty;
    }
}
