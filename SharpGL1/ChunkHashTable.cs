using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpGLCudafy
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChunkHashKey
    {
        public int hash;
        public int x;
        public int y;
        public int z;

        public override string ToString()
        {
            return $"({x}, {y}, {z}) hash={hash}";
        }
    }

    // it is a robin hood hash table implementation for read speed
    public class ChunkHashTable
    {
        List<int> hashPrimes = new List<int>() { 53, 97, 193, 389, 769, 1543, 3079, 6151, 12289, 24593, 49157, 98317, 196613, 393241, 786433, 1572869,
            3145739, 6291469, 12582917, 25165843, 50331653, 100663319, 201326611, 402653189, 805306457, 1610612741 };

        public int p1 = 3145739; // just took 'good' primes from above list for the actual hash calc too
        public int p2 = 25165843;
        public int p3 = 201326611;

        public int currentArraySize = 0;
        public int maxOffset = 0;
        public int recordCount = 0;

        public float maxLoadFactor = 0.5f;

        public ChunkHashKey[] keys;
        public ChunkData[] values;

        private object objectLock;

        public ChunkHashTable(float setMaxLoad = 0.7f)
        {
            currentArraySize = hashPrimes[5]; // 1543, to prevent early resizing
            keys = new ChunkHashKey[currentArraySize + 100];
            values = new ChunkData[currentArraySize + 100];

            maxOffset = 0;
            recordCount = 0;
            maxLoadFactor = setMaxLoad;

            for (int n = 0; n < keys.Length; n++)
            {
                keys[n].hash = -1; // empty struct indicator
            }
        }

        public double LoadFactor()
        {
            return (float)recordCount / (float)currentArraySize;
        }

        public int[] Analyze()
        {
            int[] counts = new int[maxOffset + 1];

            for (int n = 0; n < keys.Length; n++)
            {
                if (keys[n].hash != -1) // skip empty records
                {
                    counts[n - keys[n].hash]++; // calc offset
                }
            }

            return counts;
        }

        public int Hash(int x, int y, int z)
        {
            int h = 1572869;
            h ^= (h << 5) + (h >> 2) + (x * 3145739);
            h ^= (h << 5) + (h >> 2) + (y * 25165843);
            h ^= (h << 5) + (h >> 2) + (z * 2013266113);

            h = (int)((uint)h % (uint)currentArraySize);
            return h;
        }

        public ChunkData Find(int x, int y, int z)
        {
            int hash = Hash(x, y, z);
            int slotHash = hash;

            while (slotHash <= hash + maxOffset)
            {
                if (keys[slotHash].x == x && keys[slotHash].y == y && keys[slotHash].z == z)
                {
                    // key match, get value
                    return values[slotHash];
                }

                slotHash++;
            }

            // not found
            return new ChunkData();
        }

        public bool Remove(int x, int y, int z)
        {
            int hash = Hash(x, y, z);
            int slotHash = hash;

            while (slotHash <= hash + maxOffset)
            {
                if (keys[slotHash].x == x && keys[slotHash].y == y && keys[slotHash].z == z)
                {
                    // key match, remove
                    values[slotHash] = new ChunkData();
                    keys[slotHash] = new ChunkHashKey();
                    keys[slotHash].hash = -1; // clear code
                    return true;
                }

                slotHash++;
            }

            // not found
            return false;
        }

        public int Insert(int x, int y, int z, ChunkData value)
        {
            //lock (this)
            {
                int hash = Hash(x, y, z);

                int slotHash = hash;

                while (slotHash < keys.Length)
                {
                    if (keys[slotHash].hash == -1)
                    {
                        // empty slot, put data
                        keys[slotHash].hash = hash;
                        keys[slotHash].x = x;
                        keys[slotHash].y = y;
                        keys[slotHash].z = z;

                        values[slotHash] = value;

                        recordCount++;
                        break;
                    }
                    else if (keys[slotHash].x == x && keys[slotHash].y == y && keys[slotHash].z == z)
                    {
                        // key match, update value
                        // data[slotHash].hash = hash;
                        values[slotHash] = value; // replace value
                        break;
                    }
                    else if ((hash - slotHash) < (keys[slotHash].hash - slotHash))
                    {
                        BumpInsert(slotHash);

                        // replace data
                        keys[slotHash].hash = hash;
                        keys[slotHash].x = x;
                        keys[slotHash].y = y;
                        keys[slotHash].z = z;

                        values[slotHash] = value;
                        break;
                    }

                    slotHash++;
                }

                int offset = slotHash - hash;

                if (offset > maxOffset)
                {
                    maxOffset = offset; // update
                }

                if (LoadFactor() > maxLoadFactor)
                {
                    Grow();
                }

                return offset;
            }
        }

        public int BumpInsert(int index)
        {
            // item[index] is the thing being bumped
            ChunkHashKey key = keys[index];
            ChunkData value = values[index];

            int hash = key.hash;

            int slotHash = index + 1;

            while (slotHash < keys.Length)
            {
                if (keys[slotHash].hash == -1)
                {
                    // empty slot, put data
                    keys[slotHash] = key;
                    values[slotHash] = value;

                    recordCount++;
                    break;
                }
                else if ((hash - slotHash) < (keys[slotHash].hash - slotHash))
                {
                    BumpInsert(slotHash);

                    // replace data
                    keys[slotHash] = key;
                    values[slotHash] = value;
                    break;
                }

                slotHash++;
            }

            int offset = slotHash - hash;

            if (offset > maxOffset)
            {
                maxOffset = offset; // update
            }

            return offset;
        }

        public void Grow()
        {
            int newSize = hashPrimes.First(item => item > currentArraySize);

            ChunkHashKey[] tempKeys = keys.ToArray();
            ChunkData[] tempValues = values.ToArray();

            keys = new ChunkHashKey[newSize + 100];
            values = new ChunkData[newSize + 100];
            currentArraySize = newSize;

            for (int n = 0; n < keys.Length; n++)
            {
                keys[n].hash = -1; // empty struct indicator
            }

            recordCount = 0; // reset values
            maxOffset = 0;

            for (int n = 0; n < tempKeys.Length; n++)
            {
                if (tempKeys[n].hash != -1) // has data
                {                    
                    Insert(tempKeys[n].x, tempKeys[n].y, tempKeys[n].z, tempValues[n]); // recalc hash will occur on insert, insert data
                }
            }
        }
    }
}
