﻿using ilPSP;
using MPI.Wrappers;
using NUnit.Framework;

namespace BoSSS.Application.CDG_ProjectionTest {

    /// <summary>
    /// Unit test for the continuous L2-projection
    /// </summary>
    [TestFixture]
    public class AllUpTest {

      


        [Test]
        public void AllUp() {

            CDGprojectionMain p = null;

            //System.Threading.Thread.Sleep(10000);
            //ilPSP.Environment.StdoutOnlyOnRank0 = false;

            BoSSS.Solution.Application._Main(new string[0], true, delegate () {
                p = new CDGprojectionMain();
                return p;
            });

           Assert.IsTrue(p.passed);

        }
    }



}
