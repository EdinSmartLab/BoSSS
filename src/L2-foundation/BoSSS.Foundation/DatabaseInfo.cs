﻿/* =======================================================================
Copyright 2017 Technische Universitaet Darmstadt, Fachgebiet fuer Stroemungsdynamik (chair of fluid dynamics)

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

using ilPSP;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BoSSS.Foundation.IO {

    /// <summary>
    /// Standard implementation of a database info object.
    /// </summary>
    public class DatabaseInfo : IDatabaseInfo {

        /// <summary>
        /// Stores the path
        /// </summary>
        /// <param name="path">Path to the database</param>
        public DatabaseInfo(string path) {
            this.Path = path;
            if(path == null) {
                Controller = NullDatabaseController.Instance;
            } else {
                Controller = new DatabaseController(this);
            }
        }

        /// <summary>
        /// Full path to the base directory of the database.
        /// </summary>
        public string Path {
            get;
            private set;
        }

        

        /// <summary>
        /// Provides functionality to copy/move/delete info objects stored in
        /// the database
        /// </summary>
        public IDatabaseController Controller {
            get;
            private set;
        }

        /// <summary>
        /// Returns a string representation of this database.
        /// </summary>
        /// <returns></returns>
        public override string ToString() {
            // TO DO: THIS REQUIRES IO OPERATIONS => BAD!
            //return "{ Session Count = " + Controller.Sessions.Count()
            //    + "; Grid Count = " + Controller.Grids.Count()
            //    + "; Path = " + Path + " }";

            // Temporary workaround
            return "{ Session Count = " + ((DatabaseController)Controller).SessionCount
                + "; Grid Count = " + ((DatabaseController)Controller).GridCount
                + "; Path = " + Path + " }";
        }

        /// <summary>
        /// detects if some other path actually also points to this database
        /// </summary>
        public bool PathMatch(string otherPath) {
            if(this.Path == otherPath)
                return true;
            if(!Directory.Exists(otherPath))
                return false;

            string TokenName = Guid.NewGuid().ToString() + ".token";

            string file1 = System.IO.Path.Combine(this.Path, TokenName);
            File.WriteAllText(file1, "this is a test file which can be safely deleted.");

            string file2 = System.IO.Path.Combine(otherPath, TokenName);

            return File.Exists(file2);
        }

        /// <summary>
        /// The sessions of this database.
        /// </summary>
        public IList<ISessionInfo> Sessions {
            get {
                //Stopwatch stw = new Stopwatch();

                //Console.WriteLine("aquire sessions...");
                //stw.Reset();
                //stw.Start();
                var allsessions = Controller.Sessions;
                //stw.Stop();
                //Console.WriteLine("done. " + stw.ElapsedMilliseconds);

                //Console.WriteLine("sorting sessions...");
                //stw.Reset();
                //stw.Start();
                var R = allsessions.OrderByDescending(s => s.WriteTime).ToList();
                //stw.Stop();
                //Console.WriteLine("done. " + stw.ElapsedMilliseconds);

                return R;
            }
        }

        /// <summary>
        /// The grids of this database.
        /// </summary>
        public IList<IGridInfo> Grids {
            get {
                return Controller.Grids.OrderByDescending(g => g.WriteTime).ToList();
            }
        }

        /// <summary>
        /// Sessions sorted according to projects, see <see cref="ISessionInfo.ProjectName"/>.
        /// </summary>
        public IDictionary<string, IEnumerable<ISessionInfo>> Projects {
            get {
                Dictionary<string, IEnumerable<ISessionInfo>> R = new Dictionary<string, IEnumerable<ISessionInfo>>();

                foreach(var s in this.Sessions) {
                    string PrjNmn = s.ProjectName;
                    if(PrjNmn == null || PrjNmn.Length <= 0)
                        PrjNmn = "__unknown_project__";

                    IEnumerable<ISessionInfo> ProjectSessions;
                    if(!R.TryGetValue(PrjNmn, out ProjectSessions)) {
                        ProjectSessions = new List<ISessionInfo>();
                        R.Add(PrjNmn, ProjectSessions);
                    }
                    Debug.Assert(ProjectSessions != null);

                    ((List<ISessionInfo>)ProjectSessions).Add(s);
                }


                return R;
            }
        }

         /// <summary>
        /// Reference equality
        /// </summary>
        public bool Equals(IDatabaseInfo other) {
            if(object.ReferenceEquals(this, other))
                return true;


            string mName = System.Environment.MachineName.ToLowerInvariant();

            List<ValueTuple<string, string>> allPaths = new List<(string, string)>();
            allPaths.Add((other.Path, null));
            allPaths.AddRange(other.AlternateDbPaths);

            foreach(var t in allPaths) {
                string path = t.Item1;
                string filter = t.Item2;

                if(!filter.IsNullOrEmpty() && !filter.IsEmptyOrWhite()) {
                    if(!mName.Contains(filter)) {
                        continue;
                    }
                }

                if(this.PathMatch(path))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        public override bool Equals(object obj) {
            return this.Equals(obj as NullDatabaseInfo);
        }

        /// <summary>
        /// 
        /// </summary>
        public override int GetHashCode() {
            return 1; // deactivate hashing
        }


        /// <summary>
        /// Alternative paths to access the database, if <see cref="DbPath"/> is not present on a given machine.
        /// This allows to use the same control file or object on different machines, where the database is located in a different path.
        /// - 1st entry: path into the local file system
        /// - 2nd entry: optional machine name filter
        /// </summary>
        public (string DbPath, string MachineFilter)[] AlternateDbPaths {
            get {
                string p = System.IO.Path.Combine(this.Path, "AlternatePaths.txt");
                
                if(!File.Exists(p))
                    return new ValueTuple<string, string>[0];

                string[] lines = File.ReadAllLines(p);

                var ret = new List<ValueTuple<string, string>>();
                foreach(var line in lines) {
                    if(line.StartsWith(";;"))
                        continue;
                    string[] parts = line.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                    if(parts.Length >= 2) {
                        ret.Add((parts[0], parts[1]));
                    } else if(parts.Length >= 1) {
                        ret.Add((parts[0], null));
                    }
                }
                
                return ret.ToArray();
            }    
        }
    }
}
