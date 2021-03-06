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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ilPSP.LinSolvers.PARDISO {

    /// <summary>
    /// Singleton pattern to control the instantiation of the PARDISO wrapper
    /// </summary>
    public static class SingletonPARDISO
    {
        static private string parallelism = "SEQ";

        public static void SetParallelism(string si)
        {
            parallelism = si;
        }

        public static Wrapper_MKL Instance
        {
            get
            {
                return new Wrapper_MKL(parallelism);
            }
        }
    }

    /// <summary>
    /// Another wrapper layer that encapsulates 
    /// </summary>
    class MetaWrapper {

        /// <summary>
        /// ctor.
        /// </summary>
        public MetaWrapper(Version __V) {
            Init(__V);
        }

        private void Init(Version __V) {
            switch (__V) {
                case Version.MKL: if (mkl == null) mkl = SingletonPARDISO.Instance; break;
                case Version.v5: if (v5 == null) v5 = new Wrapper_v5(); break;
            }
        }


        static Wrapper_MKL mkl;

        static Wrapper_v5 v5;


        /// <summary>
        /// PARDISO interface, see PARDISO documentation
        /// </summary>
        public unsafe int PARDISOINIT(void* pt, int* mtype, int* iparm, double* dparam) {
            if (mkl != null) {
                return mkl.PARDISOINIT(pt, mtype, iparm);
            } else if (v5 != null) {
                int error = 0;
                int solver = 0; // use sparse direct
                v5.PARDISOINIT(pt, mtype, &solver, iparm, dparam, &error);
                return error;
            }
            throw new NotImplementedException();
        }

        /// <summary>
        /// PARDISO interface, see PARDISO documentation
        /// </summary>
        public unsafe int PARDISO(void* pt, int* maxfct, int* mnum, int* mtype,
                                            int* phase, int* n,
                                            void* a, int* ia, int* ja,
                                            int* perm, int* nrhs, int* iparm,
                                            int* msglvl, double* b, double* x,
                                            int* error, double* dparam) {
            //
            if (mkl != null) {
                return mkl.PARDISO(pt, maxfct, mnum, mtype, phase, n, a, ia, ja, perm, nrhs, iparm, msglvl, b, x, error);
            } else if (v5 != null) {
                v5.PARDISO(pt, maxfct, mnum, mtype, phase, n, a, ia, ja, perm, nrhs, iparm, msglvl, b, x, error, dparam);
                return *error;
            }
            throw new NotImplementedException();
        }


        public string PARDISOerror2string(int error) {
            string errStr = "";
            if (mkl != null) {
                errStr = mkl.PARDISOerror2string(error);
            } else if (v5 != null) {
                errStr = v5.PARDISOerror2string(error);
            } else {
                errStr = "unknown error, unknown PARDISO version.";
            }
            throw new ArithmeticException("PARDISO error occured: " + errStr);
        }
    }
}
