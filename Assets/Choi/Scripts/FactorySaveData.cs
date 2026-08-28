using System;
using System.Collections.Generic;

namespace Choi.SaveLoad
{
    [Serializable]
    public sealed class FactoryProgressData
    {
        public int version = 1;
        public int coreProcessorIndex = -1;
        public List<MinerProgressData> miners = new List<MinerProgressData>();
        public List<ProcessorProgressData> processors = new List<ProcessorProgressData>();
        public List<BeltProgressData> belts = new List<BeltProgressData>();
        public List<PowerNodeData> powerNodes = new List<PowerNodeData>();
    }

    [Serializable]
    public sealed class MinerProgressData
    {
        public bool exists;
        public string machineKey;
        public string outputResourceKey;
        public float baseSpeed = 1f;
        public float mineIntervalSeconds;
        public int yieldPerCycle;
        public float progress;
        public int bufferedOutput;
        public Int2Data anchor;
    }

    [Serializable]
    public sealed class ProcessorProgressData
    {
        public bool exists;
        public string machineKey;
        public string recipeKey;
        public string activeRecipeKey;
        public float baseSpeed = 1f;
        public Int2Data facing;
        public Int2Data anchor;
        public Int2Data footprint;
        public bool universalPorts;
        public bool isProcessing;
        public float progress;
        public int capacity;
        public List<ResourceStackData> input = new List<ResourceStackData>();
        public List<ResourceStackData> output = new List<ResourceStackData>();
    }

    [Serializable]
    public sealed class BeltProgressData
    {
        public bool exists;
        public int id;
        public bool hasNext;
        public int nextId;
        public float length;
        public float speed;
        public float itemSpacing;
        public bool hasSourceProcessor;
        public int sourceProcessorIndex;
        public bool hasTargetProcessor;
        public int targetProcessorIndex;
        public bool hasLockedResource;
        public string lockedResourceKey;
        public string lockedRecipeKey;
        public Int2Data cell;
        public Vector3Data start;
        public Vector3Data end;
        public List<BeltItemProgressData> items = new List<BeltItemProgressData>();
    }

    [Serializable]
    public sealed class BeltItemProgressData
    {
        public string resourceKey;
        public float position;
    }

    [Serializable]
    public sealed class ResourceStackData
    {
        public string resourceKey;
        public int amount;
    }

    [Serializable]
    public sealed class PowerNodeData
    {
        public int id;
        public int kind;
        public Int2Data cell;
    }

    [Serializable]
    public struct Int2Data
    {
        public int x;
        public int y;

        public Int2Data(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    [Serializable]
    public struct Vector3Data
    {
        public float x;
        public float y;
        public float z;

        public Vector3Data(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }
}
