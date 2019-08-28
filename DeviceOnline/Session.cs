using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace DeviceOnline
{
    /** 
    * @desc Session
    * A web request from WebServer, sends out webpages / pictures etc.
    * If its a htm page, it runs a mini template engine what can replace variables and run simple commands    * 
    * -ClientRequest, called from WebServer when a page is asked from a webbrowser
    * -runTemplate, used to return htm files after changing them with the template engine
    * -parseCommand, runs 1 command on a template (the template engine)
    * -ReplaceVariable, adds or replaces a dictionary value
    * -InjectVariables, replaces variables in the template ie $variable; with the value of variable
    * @author Martin Sykes admin@martrinex.net
    */
    class Session
    {
        public string username = null;

        // Handle a request from the WebServer, which has already setup or re-used our session and username (using cookies)
        public void ClientRequest(HttpListenerContext context, System.Data.SQLite.SQLiteConnection db)
        {
            try
            {
                HttpListenerRequest request = context.Request;
                // work out file request path
                String page = request.RawUrl.Substring(Program.prefix.Length + 1).Replace("/", "\\");
                if (page.Contains("?")) page = page.Substring(0, page.IndexOf("?"));
                if (page.EndsWith("\\")) page += "index.htm";
                // if no username restrict access to logon.htm and the style sheet.
                if (username == null)
                {
                    Console.WriteLine("Not logged on: '" + page + "'");
                    if (page != "\\style.css") page = "\\logon.htm";
                }
                String path = Program.path + Program.prefix + page;
                String ext = (page.Contains(".")) ? page.Substring(page.IndexOf(".")).ToLower() : "";
                // open and load file into buffer if file type supported and it exists...
                byte[] buffer;
                string responseString = "";
                context.Response.StatusCode = 200;
                if ((ext == ".htm" || ext == ".css" || ext == ".jpg" || ext == ".png") && File.Exists(path))
                {
                    if (ext == ".jpg" || ext == ".png")
                    {
                        // binary file load as bytes
                        buffer = File.ReadAllBytes(path);
                    }
                    else
                    {
                        // text file load all text
                        responseString = File.ReadAllText(path);
                        if (ext == ".htm")
                        {
                            // htm file put it through our template engine.
                            Console.WriteLine("Got request for url: " + request.Url); // full url
                            responseString = runTemplate(db, context, request, responseString);
                        }
                        buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                    }
                }
                else
                {
                    // not found
                    responseString = "<HTML><BODY>404 Not Found</BODY></HTML>";
                    buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                    context.Response.StatusCode = 404;
                }
                // send the response
                HttpListenerResponse response = context.Response;
                response.ContentLength64 = buffer.Length;
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                output.Close();
                Console.WriteLine("Sent: " + request.Url); // full url
            }
            catch (Exception e) { Console.WriteLine("Error:" + e.Message + "," + e.StackTrace); }
        }

        // reads a htm file looks for template code inside [] and replaces variables starting with $
        private String runTemplate(SQLiteConnection db, HttpListenerContext context, HttpListenerRequest request, String template)
        {

            String result = "";
            // setup default variables to replace
            Dictionary<string, string> data = new Dictionary<string, string>();
            data.Add("version", Program.version+"");
            data.Add("url", request.Url.ToString());
            data.Add("method", request.HttpMethod);
            data.Add("rawurl", request.RawUrl.ToString());
            data.Add("defaultpasswarning", (Program.defaultPassWarning) ? "<div class='warning'>Default password currently in use, please change admin password.</div>" : "");
            foreach (String key in request.QueryString.AllKeys) data.Add("get_" + key.ToLower(), request.QueryString.Get(key));
            SQLiteDataReader reader = new SQLiteCommand("SELECT `key`,`value` FROM `settings`", db).ExecuteReader();
            while (reader.Read()) { data.Add("setting_" + reader.GetString(0), reader.GetString(1)); }
            reader.Close();
            // get form variables if they exist
            if (request.HasEntityBody && request.HttpMethod == "POST")
            {
                data.Add("contenttype", request.ContentType);
                StreamReader input = new StreamReader(request.InputStream, request.ContentEncoding);
                string[] p = input.ReadToEnd().Split('&');
                foreach (string param in p)
                {
                    string[] pv = param.Split(new[] { '=' }, 2);
                    string pname = pv[0], pvalue = (pv.Length > 1) ? HttpUtility.UrlDecode(pv[1]) : "";
                    data.Add("post_" + pname, pvalue);
                }
            }

            // start reading the template, get everything between [] in the template
            Command c;
            int search = 0, lastoutput = 0;
            while ((c = Command.NextCommand(template, search)) != null)
            {
                // if gap between commands output the plain html, replacing any variables $v;
                if (c.start > lastoutput) result += InjectVariables(data, template.Substring(lastoutput, c.start - lastoutput), ";");
                // we have a command between [] we run it with parseCommand
                string newvalue = parseCommand(db, request, c, data, ref template);
                result += newvalue;
                search = lastoutput = c.end;
            }
            // if gap between commands output the plain html, replacing any variables $v;
            if (lastoutput < template.Length) result += InjectVariables(data, template.Substring(lastoutput), ";");

            return result;
        }

        // run 1 command between braces []
        private String parseCommand(SQLiteConnection db, HttpListenerRequest request, Command command, Dictionary<string, string> data, ref string template)
        {
            // add variables in at the start, this can help for queries etc what need to be dynamic
            command.value = InjectVariables(data, command.value, "");
            string returnvariable = null;
            // look for commands
            switch (command.command)
            {
                // checks a condition and errases future commands if condition is FALSE
                case "if":
                case "blockif":
                    string query = null;
                    // support equals or not equals only
                    if (command.value.Contains("==")) query = "==";
                    else if (command.value.Contains("!=")) query = "!=";
                    if (query != null)
                    {
                        // get both sides of the condition, remove quotes as they not needed
                        string[] p1 = command.value.Split(new[] { query }, StringSplitOptions.None);
                        for (int i = 0; i < p1.Length; i++) if (p1[i].StartsWith("'") || p1[i].StartsWith("\"")) p1[i] = p1[i].Substring(1, p1[i].Length - 2);
                        bool qresult = ((query == "==" && p1[0] == p1[1]) || (query == "!=" && p1[0] != p1[1]));
                        if (!qresult)
                        {
                            // erase next command if 'if' statement and all commands until 'endif' if 'blockif' statement.
                            int search = command.end;
                            Command nextCommand;
                            while ((nextCommand = Command.NextCommand(template, search)) != null)
                            {
                                template = nextCommand.Inject(template, "");
                                if (nextCommand.command == "endif" || command.command == "if") break;
                                search = nextCommand.end;
                            }
                        }
                    }
                    command.value = "";
                    break;
                case "foreach": //expects SELECT query with 1 column.
                    // loops through results, gets first column and saves it as $_; (like powershell) then runs any code in the loop
                    SQLiteDataReader foreach_r = new SQLiteCommand(command.value, db).ExecuteReader();
                    command.value = "";
                    Command fe_nextCommand;
                    // loop through SELECT results
                    while (foreach_r.Read())
                    {
                        fe_nextCommand = command;
                        if (data.ContainsKey("_")) data.Remove("_"); // add variable for the column as $_;
                        data.Add("_", Convert.ToString(foreach_r[0]));
                        // loop through the command in the foreach/endeach block
                        while ((fe_nextCommand = Command.NextCommand(template, fe_nextCommand.end)) != null)
                        {
                            command.value += parseCommand(db, request, fe_nextCommand, data, ref template);
                            if (fe_nextCommand.command == "endeach") { break; }
                        }
                    }
                    foreach_r.Close();
                    // skip to the end of the loop regardless if it ran or not
                    fe_nextCommand = command;
                    while ((fe_nextCommand = Command.NextCommand(template, fe_nextCommand.end)) != null)
                    {
                        if (fe_nextCommand.command == "endeach") { command.end = fe_nextCommand.end; break; }
                    }
                    break;
                case "hash":
                    // hash and salt a string, usually a password before saving to database
                    if (data.ContainsKey(command.value))
                    {
                        string value = Program.GetHashString(data[command.value]);
                        data.Remove(command.value);
                        data.Add(command.value, value);
                    }
                    command.value = "";
                    break;
                case "initsettings":
                    // if mail server settings change we can reconnect so to speak without restarting the whole program
                    command.value = "";
                    DB.initSettings();
                    break;
                case "securesetting":
                case "setting": // [setting:name=value]
                    // save something to the database settings table
                    string[] p2 = command.value.Split('=');
                    if (p2.Length == 2)
                    {
                        // strip speach marks
                        for (int i = 0; i < p2.Length; i++) if (p2[i].StartsWith("'") || p2[i].StartsWith("\"")) p2[i] = p2[i].Substring(1, p2[i].Length - 2);
                        // if securestring to store a encrypted (not hashed string) which can be decrypted (by the same user), we need this for the smtp password.
                        if (command.command == "securesetting") p2[1] = Convert.ToBase64String(ProtectedData.Protect(Encoding.Unicode.GetBytes(p2[1]), Encoding.UTF8.GetBytes(Program.salt), DataProtectionScope.CurrentUser));
                        DB.setSetting(p2[0], p2[1]);
                        // make setting immediately avaliable/updates to the template
                        if (data.ContainsKey("setting_" + p2[0])) data.Remove("setting_" + p2[0]);
                        data.Add("setting_" + p2[0], DB.getSetting(p2[0]));
                    }
                    command.value = "";
                    break;
                case "endpage":
                    // quickly end template, bit of a hack but allows to do redirects for logging in etc
                    command.value = "";
                    template = template.Substring(0, command.end);
                    break;
                case "logon":
                    // check username and password against database
                    string username = data["post_username"].Replace("`", "").Replace("'", "");
                    string password = Program.GetHashString(data["post_password"]); // must be hashed and salted
                    int r = new SQLiteCommand("UPDATE `users` SET `laston`=CURRENT_TIMESTAMP WHERE `username`='" + username + "' AND `password`='" + password + "'", db).ExecuteNonQuery();
                    bool logonresult = (r == 1);
                    if (logonresult) this.username = username.ToLower();
                    if (command.value != "")
                    {
                        data.Remove(command.value);
                        data.Add("r", Convert.ToString(logonresult));
                        command.value = "";
                    }
                    break;
                case "endif":
                case "echo":
                    // write directly to output (by not erasing command.value)
                    break;
                case "set":
                case "default":
                    // setup a variable and make avaliable to template
                    if (!command.value.Contains(":=")) command.value = command.value.Replace("=", ":=");
                    if (command.value.Contains(":="))
                    {
                        returnvariable = command.value.Substring(0, command.value.IndexOf(":="));
                        command.value = command.value.Substring(returnvariable.Length + 2).Trim();
                        if (command.value.StartsWith("'") || command.value.StartsWith("\"")) command.value = command.value.Substring(1, command.value.Length - 2);
                        if (command.command == "set" && data.ContainsKey(returnvariable)) data.Remove(returnvariable);
                        if (!data.ContainsKey(returnvariable)) data.Add(returnvariable, command.value);
                    }
                    command.value = "";
                    break;
                case "option":
                    // setup a html option form dropdown whatever, and select the default value if it exists
                    string[] p = command.value.Split(',');
                    string def = p[0];
                    if (p.Length > 1 && p[1].StartsWith("query:"))
                    {
                        // setup from database
                        string sqloption = command.value.Substring(def.Length + 1 + "query:".Length);
                        SQLiteDataReader reader_option = new SQLiteCommand(sqloption, db).ExecuteReader();
                        command.value = "";
                        while (reader_option.Read())
                        {
                            string ov = reader_option.GetString(0), ot = reader_option.GetString(1);
                            string os = (def == ov) ? "selected" : "";
                            command.value += "<option value='" + ov + "' " + os + ">" + ot + "</option>";
                        }
                        reader_option.Close();
                    }
                    else
                    {
                        // seutp from static list
                        command.value = "";
                        for (int i = 1; i < p.Length; i++)
                        {
                            string[] o = p[i].Split('#');
                            string ov = o[0], ot = (o.Length > 1) ? o[1] : ov, os = (def == ov) ? "selected" : "";
                            command.value += "<option value='" + ov + "' " + os + ">" + ot + "</option>";
                        }
                    }
                    break;
                case "query":
                    // query the sqlite database
                    if (command.value.Contains(":="))
                    {
                        returnvariable = command.value.Substring(0, command.value.IndexOf(":="));
                        command.value = command.value.Substring(returnvariable.Length + 2);
                    }
                    SQLiteCommand sql = new SQLiteCommand(command.value, db);
                    try
                    {
                        // SELECT query, expect multiple rows of results, we take the first row, create a table and a headerless table
                        if (command.value.Contains("SELECT") && returnvariable != null)
                        {
                            string datavalue = ""; string alt = "", header = ""; bool firstrow = true;
                            SQLiteDataReader queryr = sql.ExecuteReader();
                            // loop through all rows
                            while (queryr.Read())
                            {
                                datavalue += "<tr class='" + alt + "'>";
                                // loop columns, create html row
                                for (int c = 0; c < queryr.FieldCount; c++)
                                {
                                    datavalue += "<td>" + Convert.ToString(queryr[c]) + "</td>";
                                    // for the first row we add each column to the end of the return variable $return_columnname
                                    if (firstrow) ReplaceVariable(data, returnvariable + "_" + queryr.GetName(c).ToLower(), Convert.ToString(queryr[c]));
                                }
                                datavalue += "</tr>"; alt = (alt == "") ? "alt" : "";
                                firstrow = false;
                            }
                            // create the top row for $return_table
                            for (int c = 0; c < queryr.FieldCount; c++) header += "<td>" + queryr.GetName(c) + "</td>";
                            queryr.Close();
                            // output the tables
                            ReplaceVariable(data, returnvariable + "_rows", datavalue);
                            datavalue = "<table><thead><tr>" + header + "</thead><tbody>" + datavalue + "</tr></tbody></table>";
                            ReplaceVariable(data, returnvariable + "_table", datavalue);
                        }
                        else
                        {
                            // run a query (not select) such as INSERT / UPDATE, get the result # of rows edited.
                            int queryr = sql.ExecuteNonQuery();
                            sql.Dispose();
                            if (returnvariable != null) data.Add(returnvariable, Convert.ToString(queryr));
                        }
                        command.value = "";
                    }
                    catch (Exception ex) { Console.WriteLine("[Query error '" + command.value + "':" + ex.Message + "]"); }
                    break;
                case "pie": // size, margin, percent, filename
                    // save a png for a pie chart, only supports 1 percent value
                    string[] slice = command.value.Split(',');
                    int size = Convert.ToInt32(slice[0]);
                    int margin = Convert.ToInt32(slice[1]);
                    if (slice[2].EndsWith("%")) slice[2] = slice[2].Substring(0, slice[2].Length - 1);
                    float percent = (float)Convert.ToDouble(slice[2]);
                    string filename = slice[3];
                    string des = (slice.Length > 4) ? slice[4] : "";
                    bool show = (slice.Length > 5) ? Convert.ToBoolean(slice[5]) : true;
                    WebServer.BakePie("graphs\\" + filename, Brushes.Red, Brushes.Green, (float)((percent / 100.0) * 360.0), size, margin, null);
                    // we output the html img if user didn't save to hide the output
                    command.value = (show) ? "<img src='graphs/" + filename + "' alt='" + des + "' title='" + des + "' />" : "";
                    break;
                case "dashboardshow":
                    // hard coded dashboard as too much for template to do.
                    // gets tables for each group and puts them on newspaper columns on the home page
                    string dbsql = "SELECT `groups`.`name` AS `group`, `groups`.`rowid` AS `groupid`,`devices`.`name`,`devices`.`online`,`devices`.`lastonline`,`devices`.`rowid` FROM `devices`,`groups` WHERE `groups`.`dashboardid`!=0 AND `devices`.`groupid`=`groups`.`rowid` ORDER BY `groups`.`dashboardid`";
                    SQLiteDataReader dbreader = new SQLiteCommand(dbsql, db).ExecuteReader();
                    command.value = "";
                    string lastgroup = "";
                    while (dbreader.Read())
                    {
                        // read each device
                        string groupname = dbreader.GetString(0);
                        int dbgroupid = dbreader.GetInt32(1);
                        string devicename = dbreader.GetString(2);
                        bool online = dbreader.GetBoolean(3);
                        DateTime timestamp = (dbreader.IsDBNull(4) ? DateTime.MinValue : dbreader.GetDateTime(4));
                        int rowid = dbreader.GetInt32(5);
                        string pic = (online) ? "online.png" : "offline.png";
                        string date = (timestamp == DateTime.MinValue ? "&nbsp;" : Convert.ToString(timestamp));
                        if (online) date = "&nbsp;";
                        if (lastgroup != groupname)
                        {
                            // everytime the groupname changes, create a new table
                            if (lastgroup != "") command.value += "</table>";
                            command.value += "<table with='100%' style='margin:8px;column-break-inside:avoid;display: inline-flex;'><tr><td colspan='3'><div class='blueheading'>&nbsp;<a href='groupuptime.htm?id=" + dbgroupid + "'>" + groupname + "</a></div></tr></td>";
                            lastgroup = groupname;
                        }
                        command.value += "<tr><td width='0%'><img src='" + pic + "' /></td><td align='left'><a href='deviceuptime.htm?id=" + rowid + "'>" + devicename + "</a></td><td align='left'>" + date + "</td></tr>";
                    }
                    command.value += "</table>";
                    dbreader.Close();
                    break;
                case "dashboard":
                    // hard coded, for the settings page, to move groups around the dashboard screen
                    string dashboardjob = command.value.Split(':')[0];
                    int groupid = Convert.ToInt32(command.value.Split(':')[1]), changes = 0;
                    int currentvalue = Convert.ToInt32(new SQLiteCommand("SELECT `dashboardid` FROM `groups` WHERE `rowid`=" + groupid, db).ExecuteScalar());
                    if (dashboardjob == "remove" && currentvalue != 0)
                    {
                        // hide a group from the dashboard by setting its dashboardid to 0
                        changes = new SQLiteCommand("UPDATE `groups` SET `dashboardid`=0 WHERE `rowid`=" + groupid + ";UPDATE `groups` SET `dashboardid`=`dashboardid`-1 WHERE `dashboardid`>0 AND `dashboardid`>" + currentvalue, db).ExecuteNonQuery();
                    }
                    else if (dashboardjob == "down")
                    {
                        // move group down
                        if (currentvalue == 0)
                        {
                            // if its hidden increment all other groups what arn't hidden and set this group to 1 (0+1=1)
                            changes = new SQLiteCommand("UPDATE `groups` SET `dashboardid`=`dashboardid`+1 WHERE `dashboardid`!=0 OR `rowid`=" + groupid, db).ExecuteNonQuery();
                        }
                        else
                        {
                            // if its not hidden switch the position with the next inline
                            changes = new SQLiteCommand("UPDATE `groups` SET `dashboardid`=`dashboardid`-1 WHERE `dashboardid`=" + (currentvalue + 1) + ";", db).ExecuteNonQuery();
                            changes += new SQLiteCommand("UPDATE `groups` SET `dashboardid`=`dashboardid`+1 WHERE `rowid`= " + groupid + ";", db).ExecuteNonQuery();
                        }
                    }
                    else if (dashboardjob == "up" && currentvalue > 1)
                    {
                        // move group up by switching positions with the next inline
                        changes = new SQLiteCommand("UPDATE `groups` SET `dashboardid`=`dashboardid`+1 WHERE `dashboardid`=" + (currentvalue - 1) + ";", db).ExecuteNonQuery();
                        changes += new SQLiteCommand("UPDATE `groups` SET `dashboardid`=`dashboardid`-1 WHERE `rowid`= " + groupid + ";", db).ExecuteNonQuery();
                    }
                    command.value = "<div class='notice'>" + ((changes == 0) ? "Failed to update groups" : "Group dashboard order, updated.") + "</div>";
                    break;
            }
            return command.value;
        }

        // go through 'text' and replace any variables we have, ending allows for an optional ';' type thing after the variable.
        private string InjectVariables(Dictionary<string, string> variables, string text, string ending)
        {
            if (!text.Contains('$')) return text; // no variables, don't bother
            List<string> variableskeys = new List<string>(variables.Keys);
            foreach (string key in variableskeys) text = text.Replace("$" + key + ending, variables[key]);
            return text;
        }
        // updates dictionary with a new value or replaces the old value if it exists already.
        private void ReplaceVariable(Dictionary<string, string> data, string name, string value)
        {
            if (data.ContainsKey(name)) data.Remove(name);
            data.Add(name, value);
        }
    }

    /** 
    * @desc Command, template engine running code/basic commands between braces [] 
    * NextCommand, finds the next brace on a page[], page the template, startindex where to look from.
    * Inject, removes the command from the template, to prevent running it again
    * @author Martin Sykes admin@martrinex.net
    */
    class Command
    {
        public string command, value = "";
        public int start, end;
        public static Command NextCommand(string page, int startindex)
        {
            // get between both braces []
            int brace = page.IndexOf('[', startindex);
            if (brace < 0) return null;
            int braceend = page.IndexOf(']', brace);
            if (braceend < 0) return null;
            Command output = new Command();
            string bracevalue = page.Substring(brace + 1, braceend - brace - 1);
            // check if command has a [command:value] seperated by colon.
            int seperator = bracevalue.IndexOf(':');
            if (seperator < 0)
            {
                output.command = bracevalue;
            }
            else
            {
                output.command = bracevalue.Substring(0, seperator);
                output.value = bracevalue.Substring(seperator + 1);
            }
            // say where the brace is so we can replace / skip past it to the next brace etc
            output.start = brace;
            output.end = braceend + 1;
            return output;
        }
        // Inject, replaces a command on the template, only used to remove commands on if statements nowadays
        public string Inject(string page, string newvalue)
        {
            if (newvalue == null) newvalue = "";
            string output = page.Substring(0, start) + newvalue + page.Substring(end);
            end = start + newvalue.Length;
            return output;
        }
    }
}
