#!/usr/bin/bash

cd $BOSSS_INSTALL/public/src/L3-solution/BoSSS.Solution/

sed -i -e "s:AssemblyInformationalVersion(\".*\"):AssemblyInformationalVersion(\"$(git rev-parse HEAD)\"):g" ./Properties/AssemblyInfo.cs