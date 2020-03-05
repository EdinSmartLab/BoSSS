﻿using System;
using System.IO;

namespace bcl.init_db {

    /// <summary>
    /// Main class of the init-db ("init database") program which creates the
    /// directory structure of a BoSSS database (<see cref="Execute"/>).
    /// Additionally, it registers the database in the DBE config if possible.
    /// </summary>
    class Program : IProgram {

        /// <summary>
        /// The directory where we want to init the database
        /// </summary>
        private DirectoryInfo targetDirectory;

        #region IProgram Member

        /// <summary>
        /// Create the BoSSS database directory structure in
        /// <see cref="targetDirectory"/> and registers the directory in the
        /// DBE config (<see cref="register_db.Program"/>). The structure is:
        /// ./data
        /// ./timesteps
        /// ./grids
        /// ./sessions
        /// </summary>
        public void Execute() {
            if (!targetDirectory.Exists) {
                try {
                    targetDirectory.Create();
                } catch (SystemException e) {
                    throw new UserInputException("Could not create directory at given path.", e);
                }
            }
            
            // Create structure
            Directory.CreateDirectory(Path.Combine(targetDirectory.FullName, "data"));
            Directory.CreateDirectory(Path.Combine(targetDirectory.FullName, "timesteps"));
            Directory.CreateDirectory(Path.Combine(targetDirectory.FullName, "grids"));
            Directory.CreateDirectory(Path.Combine(targetDirectory.FullName, "sessions"));

            // Create 'AlternatePaths.txt'
            using(var stw = File.CreateText(Path.Combine(targetDirectory.FullName, "AlternatePaths.txt"))) {
                stw.WriteLine(";; Add alternative paths for this database (on a different computer) here;");
                stw.WriteLine(";; The format is:");
                stw.WriteLine(";; path[,machine-filter]");
                stw.WriteLine(";; ");
                stw.WriteLine(";; E.g. suppose a database which is stored on a remote linux system namned as 'smurf.domain.com' under '/home/asrael/bosss_db'.");
                stw.WriteLine(";; On the local workstation '/home/asrael' is mounted (e.h. via sshfs) as 'X:'.");
                stw.WriteLine(";; then, the entries here may look (with out leading comment ;; marker)as: ");
                stw.WriteLine(";; ");
                stw.WriteLine(";; /home/asrael/bosss_db,smurf");
                stw.WriteLine(";; X:\\bosss_db");
                stw.Flush();
            }
            // Register it
            register_db.Program p = new register_db.Program();
            p.DecodeArgs(new string[] { targetDirectory.FullName });
            p.Execute();
        }

        /// <summary>
        /// Checks the arguments for an insallation path.
        /// Case 1: No args -> Take current directory
        /// Case 2: One arg -> args[0] is the relative path to the directory
        /// </summary>
        /// <param name="args">
        /// A list of arguments (either empty or containing exactly one
        /// element)
        /// </param>
        public void DecodeArgs(string[] args) {
            if (args.Length > 1) {
                throw new UserInputException("Too many arguments");
            } else if (args.Length == 1) {
                targetDirectory = new DirectoryInfo(args[0]);
            } else {
                targetDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
            }
        }

        /// <summary>
        /// See <see cref="IProgram.PrintUsage"/>.
        /// </summary>
        public void PrintUsage() {
            Console.WriteLine("Usage: bcl init-db [$target]");
            Console.WriteLine();
            Console.WriteLine("Use $target to specify a location for the database. Default: $target=.");
            Console.WriteLine();
        }

        #endregion
    }
}
