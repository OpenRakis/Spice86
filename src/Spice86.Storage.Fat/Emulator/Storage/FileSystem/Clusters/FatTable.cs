namespace Spice86.Shared.Emulator.Storage.FileSystem.Clusters;

using System;
using System.Collections.Generic;

using Spice86.Shared.Emulator.Storage.FileSystem;

/// <summary>
/// Thrown when a FAT cluster chain is malformed (cycle, out-of-range link, bad-cluster in chain).
/// </summary>
public sealed class FatChainCorruptionException : Exception {
    /// <summary>Initialises a new instance with a diagnostic message.</summary>
    /// <param name="message">Description of the corruption.</param>
    public FatChainCorruptionException(string message) : base(message) {
    }
}

/// <summary>
/// In-memory mutable FAT cluster table. Wraps a <c>uint[]</c> indexed by cluster number,
/// with explicit semantics for allocate / free / link / follow operations.
/// </summary>
/// <remarks>
/// Clusters 0 and 1 are reserved per the FAT specification (cluster 0 stores the media
/// descriptor, cluster 1 traditionally stores 0xFFF/0xFFFF/0x0FFFFFFF). Data clusters
/// start at index 2.
/// </remarks>
public sealed class FatTable {
    private readonly uint[] _entries;
    private readonly FatType _fatType;
    private readonly uint _maxClusterValue;

    /// <summary>
    /// Initialises an empty FAT table containing <paramref name="clusterCount"/> entries
    /// (including the two reserved entries at index 0 and 1).
    /// </summary>
    /// <param name="clusterCount">Total number of cluster entries (must be at least 3).</param>
    /// <param name="fatType">FAT type controlling end-of-chain markers and value width.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="clusterCount"/> is less than 3.</exception>
    public FatTable(int clusterCount, FatType fatType) {
        if (clusterCount < 3) {
            throw new ArgumentOutOfRangeException(nameof(clusterCount), "FAT table must hold at least 3 entries.");
        }
        _entries = new uint[clusterCount];
        _fatType = fatType;
        _maxClusterValue = (uint)(clusterCount - 1);
    }

    /// <summary>FAT type for this table.</summary>
    public FatType FatType => _fatType;

    /// <summary>Total number of cluster slots, including the two reserved entries at index 0 and 1.</summary>
    public int ClusterCount => _entries.Length;

    /// <summary>Number of free (zero-valued) data clusters.</summary>
    public int FreeClusterCount {
        get {
            int free = 0;
            for (int i = 2; i < _entries.Length; i++) {
                if (_entries[i] == 0) {
                    free++;
                }
            }
            return free;
        }
    }

    /// <summary>Number of used (non-zero, non-bad) data clusters.</summary>
    public int UsedClusterCount {
        get {
            int used = 0;
            for (int i = 2; i < _entries.Length; i++) {
                uint v = _entries[i];
                if (v != 0 && !FatClusterCodec.IsBadCluster(v, _fatType)) {
                    used++;
                }
            }
            return used;
        }
    }

    /// <summary>Indexer giving direct read access to a cluster entry.</summary>
    /// <param name="cluster">Cluster number.</param>
    /// <returns>Cluster entry value.</returns>
    public uint this[uint cluster] {
        get {
            CheckRange(cluster);
            return _entries[cluster];
        }
    }

    /// <summary>Sets the cluster entry value directly. Reserved entries (0 and 1) cannot be modified through this method.</summary>
    /// <param name="cluster">Cluster number (must be at least 2).</param>
    /// <param name="value">New cluster value.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="cluster"/> is reserved or out of range.</exception>
    public void WriteEntry(uint cluster, uint value) {
        if (cluster < 2) {
            throw new ArgumentOutOfRangeException(nameof(cluster), "Cannot modify reserved cluster entries 0 or 1.");
        }
        CheckRange(cluster);
        _entries[cluster] = value;
    }

    /// <summary>Returns true if cluster <paramref name="cluster"/> is currently free (value 0).</summary>
    public bool IsFree(uint cluster) {
        CheckRange(cluster);
        return _entries[cluster] == 0;
    }

    /// <summary>Returns true if cluster <paramref name="cluster"/> currently holds an end-of-chain marker.</summary>
    public bool IsEndOfChain(uint cluster) {
        CheckRange(cluster);
        return FatClusterCodec.IsEndOfChain(_entries[cluster], _fatType);
    }

    /// <summary>Returns true if cluster <paramref name="cluster"/> is marked as bad.</summary>
    public bool IsBad(uint cluster) {
        CheckRange(cluster);
        return FatClusterCodec.IsBadCluster(_entries[cluster], _fatType);
    }

    /// <summary>
    /// Marks <paramref name="cluster"/> as end-of-chain using the canonical marker for the FAT type.
    /// </summary>
    public void MarkAsEof(uint cluster) {
        WriteEntry(cluster, FatClusterCodec.EndOfChainMarker(_fatType));
    }

    /// <summary>
    /// Finds and reserves the first free cluster, marking it as end-of-chain.
    /// </summary>
    /// <returns>The newly allocated cluster number.</returns>
    /// <exception cref="InvalidOperationException">If the volume is full.</exception>
    public uint AllocateCluster() {
        for (uint i = 2; i < _entries.Length; i++) {
            if (_entries[i] == 0) {
                _entries[i] = FatClusterCodec.EndOfChainMarker(_fatType);
                return i;
            }
        }
        throw new InvalidOperationException("FAT is full; no free clusters available.");
    }

    /// <summary>
    /// Frees <paramref name="cluster"/> by zeroing its entry. The caller is responsible for
    /// also patching any cluster whose entry pointed at this one.
    /// </summary>
    public void FreeCluster(uint cluster) {
        WriteEntry(cluster, 0);
    }

    /// <summary>
    /// Links <paramref name="head"/> to point at <paramref name="tail"/>, so that following
    /// the chain from <paramref name="head"/> next visits <paramref name="tail"/>.
    /// </summary>
    public void LinkClusters(uint head, uint tail) {
        if (tail < 2) {
            throw new ArgumentOutOfRangeException(nameof(tail), "Tail must be a data cluster (>= 2).");
        }
        CheckRange(tail);
        WriteEntry(head, tail);
    }

    /// <summary>
    /// Returns the sequence of cluster numbers reached from <paramref name="start"/>, in order,
    /// stopping at the end-of-chain marker. Detects cycles and bad clusters.
    /// </summary>
    /// <param name="start">First cluster of the chain.</param>
    /// <returns>List of cluster numbers including <paramref name="start"/>.</returns>
    /// <exception cref="FatChainCorruptionException">On cycle, free entry inside chain, bad cluster, or out-of-range link.</exception>
    public IReadOnlyList<uint> FollowChain(uint start) {
        if (start < 2 || start > _maxClusterValue) {
            throw new ArgumentOutOfRangeException(nameof(start), $"Chain start {start} is not a data cluster.");
        }
        List<uint> chain = new();
        HashSet<uint> visited = new();
        uint cursor = start;
        while (true) {
            if (!visited.Add(cursor)) {
                throw new FatChainCorruptionException($"Cycle detected at cluster {cursor}.");
            }
            chain.Add(cursor);
            uint next = _entries[cursor];
            if (FatClusterCodec.IsEndOfChain(next, _fatType)) {
                return chain;
            }
            if (next == 0) {
                throw new FatChainCorruptionException($"Free cluster encountered inside chain at {cursor}.");
            }
            if (FatClusterCodec.IsBadCluster(next, _fatType)) {
                throw new FatChainCorruptionException($"Bad cluster marker encountered inside chain at {cursor}.");
            }
            if (next < 2 || next > _maxClusterValue) {
                throw new FatChainCorruptionException($"Cluster {cursor} points at out-of-range cluster {next}.");
            }
            cursor = next;
        }
    }

    /// <summary>Returns the number of clusters in the chain starting at <paramref name="start"/>.</summary>
    public int GetChainLength(uint start) {
        return FollowChain(start).Count;
    }

    /// <summary>
    /// Loads cluster entries from raw FAT bytes using <see cref="FatClusterCodec"/>.
    /// </summary>
    /// <param name="fatBytes">Raw FAT bytes.</param>
    /// <param name="fatType">FAT type used for decoding.</param>
    /// <param name="clusterCount">Total cluster slot count (including reserved 0 and 1).</param>
    /// <returns>Populated <see cref="FatTable"/>.</returns>
    public static FatTable FromBytes(ReadOnlySpan<byte> fatBytes, FatType fatType, int clusterCount) {
        FatTable table = new(clusterCount, fatType);
        for (uint i = 0; i < clusterCount; i++) {
            table._entries[i] = FatClusterCodec.Read(fatBytes, i, fatType);
        }
        return table;
    }

    /// <summary>
    /// Serialises this table into the supplied buffer using <see cref="FatClusterCodec"/>.
    /// </summary>
    /// <param name="destination">Destination FAT bytes. Must be large enough to hold every cluster entry.</param>
    public void WriteTo(Span<byte> destination) {
        for (uint i = 0; i < _entries.Length; i++) {
            FatClusterCodec.Write(destination, i, _entries[i], _fatType);
        }
    }

    private void CheckRange(uint cluster) {
        if (cluster > _maxClusterValue) {
            throw new ArgumentOutOfRangeException(nameof(cluster), $"Cluster {cluster} is past end of FAT (max {_maxClusterValue}).");
        }
    }
}
