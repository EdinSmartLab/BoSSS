﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BoSSS.Foundation.Voronoi
{
    public class ArrayMap
    {
        int[] map;

        public ArrayMap(int[] map)
        {
            this.map = map;
        }

        public int this[int i] {
            get { return map[i]; }
            set { map[i] = value;}
        }

        public bool IsBijective()
        {
            throw new NotImplementedException();
            bool[] isPresent = new bool[map.Length];
            //Check if all links are unique
            for (int i = 0; i < map.Length; ++i)
            {
                if(isPresent[map[i]] == null)
                {
                    isPresent[map[i]] = true;
                }
                else
                {
                    return false;
                }   
            }
            return true;
        }

        public int MaxIndice()
        {
            throw new NotImplementedException();
            return 2;
        }
    }

    public class OneWayArrayMap
    {
        public ArrayConnection[] Map;

        public int Length {
            get {
                return Map.Length;
            }
        }

        public OneWayArrayMap() { }

        public OneWayArrayMap(ArrayConnection[] map)
        {
            this.Map = map;
        }

        public ArrayConnection this[int j] {
            get { return Map[j]; }
            set { Map[j] = value; }
        }

        public static OneWayArrayMap CreateEmpty(Connection type, int length)
        {
            ArrayConnection[] map = new ArrayConnection[length];
            for (int i = 0; i < length; ++i)
            {
                map[i] = new ArrayConnection
                {
                    Type = type,
                    J = -1
                };
            }
            return new OneWayArrayMap(map);
        }

        public static OneWayArrayMap Combine(OneWayArrayMap a, OneWayArrayMap b)
        {
            throw new NotImplementedException();
        }

        public void AddReverse(OneWayArrayMap towardsThis)
        {
            for (int i = 0; i < towardsThis.Length; ++i)
            {
                ArrayConnection towardsConnection = towardsThis[i];
                if (towardsConnection.Type == Connection.Remained)
                {
                    ArrayConnection connection;
                    connection.J = i;
                    connection.Type = Connection.Remained;
                    Map[towardsConnection.J] = connection;
                }
            }
        }
    }

    public enum Connection { Created, Removed, Remained };

    public struct ArrayConnection
    {
        public Connection Type;

        public int J;
    }
}
