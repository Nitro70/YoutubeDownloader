using System.Buffers.Binary;
using System.Text;

namespace YouTubeDownloader.Core.Mp4;

/// <summary>One sample (video frame or audio packet) and where its bytes live in the source file.</summary>
internal struct Mp4Sample
{
    public long Offset;
    public uint Size;
    public uint Duration;
    public int CompositionOffset;
    public bool IsSync;
}

/// <summary>An edit list entry, in the source file's movie timescale.</summary>
internal readonly record struct Mp4Edit(ulong SegmentDuration, long MediaTime, short RateInteger, short RateFraction);

/// <summary>Everything needed to copy one track into a new MP4.</summary>
internal sealed class Mp4Track
{
    public string SourcePath = "";
    public string Handler = "";
    public uint Timescale;
    public uint MovieTimescale;
    public ushort Language = 0x55C4; // "und"
    public uint Width;  // 16.16 fixed point, from tkhd
    public uint Height; // 16.16 fixed point, from tkhd
    public byte[] SampleDescriptions = Array.Empty<byte>(); // the whole stsd box, copied as-is
    public List<Mp4Edit> Edits = new();
    public List<Mp4Sample> Samples = new();

    public ulong MediaDuration
    {
        get
        {
            ulong total = 0;
            foreach (var s in Samples) total += s.Duration;
            return total;
        }
    }
}

/// <summary>
/// Reads the first track of an MP4 file into a flat sample list. Handles both ordinary MP4s
/// (sample tables in moov) and fragmented MP4s (moof/mdat pairs, as YouTube serves its
/// separate video and audio streams).
/// </summary>
internal static class Mp4TrackReader
{
    private const uint NonSyncSample = 0x10000;

    public static Mp4Track Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        var track = new Mp4Track { SourcePath = path };
        uint trackId = 0;
        var trex = new TrackExtends();
        bool haveMoov = false;

        foreach (var box in Boxes.InStream(fs, 0, fs.Length))
        {
            if (box.Type == "moov")
            {
                byte[] moov = Boxes.ReadPayload(fs, box);
                ParseMoov(moov, track, out trackId, ref trex);
                haveMoov = true;
            }
            else if (box.Type == "moof")
            {
                if (!haveMoov) throw new InvalidDataException("moof before moov");
                byte[] moof = Boxes.ReadPayload(fs, box);
                ParseMoof(moof, box.Position, track, trackId, trex);
            }
        }

        if (!haveMoov) throw new InvalidDataException($"No moov box in {Path.GetFileName(path)}");
        if (track.Samples.Count == 0) throw new InvalidDataException($"No samples in {Path.GetFileName(path)}");
        if (track.SampleDescriptions.Length == 0) throw new InvalidDataException("No stsd box");
        return track;
    }

    private struct TrackExtends
    {
        public uint DescriptionIndex, Duration, Size, Flags;
    }

    private static void ParseMoov(byte[] b, Mp4Track track, out uint trackId, ref TrackExtends trex)
    {
        trackId = 0;
        bool haveTrak = false;
        var flat = new FlatTables();

        foreach (var box in Boxes.InBuffer(b, 0, b.Length))
        {
            switch (box.Type)
            {
                case "mvhd":
                {
                    int p = box.PayloadStart;
                    byte version = b[p];
                    track.MovieTimescale = BE.U32(b, p + (version == 1 ? 20 : 12));
                    break;
                }
                case "trak" when !haveTrak:
                    haveTrak = true;
                    trackId = ParseTrak(b, box, track, flat);
                    break;
                case "mvex":
                    foreach (var mv in Boxes.InBuffer(b, box.PayloadStart, box.End))
                    {
                        if (mv.Type != "trex") continue;
                        int p = mv.PayloadStart + 4;
                        uint id = BE.U32(b, p);
                        if (id != trackId && trackId != 0) continue;
                        trex.DescriptionIndex = BE.U32(b, p + 4);
                        trex.Duration = BE.U32(b, p + 8);
                        trex.Size = BE.U32(b, p + 12);
                        trex.Flags = BE.U32(b, p + 16);
                    }
                    break;
            }
        }

        if (!haveTrak) throw new InvalidDataException("No trak box");
        flat.AppendSamples(track.Samples);
    }

    private static uint ParseTrak(byte[] b, Boxes.Box trak, Mp4Track track, FlatTables flat)
    {
        uint trackId = 0;
        foreach (var box in Boxes.InBuffer(b, trak.PayloadStart, trak.End))
        {
            switch (box.Type)
            {
                case "tkhd":
                {
                    int p = box.PayloadStart;
                    bool v1 = b[p] == 1;
                    trackId = BE.U32(b, p + (v1 ? 20 : 12));
                    track.Width = BE.U32(b, p + (v1 ? 88 : 76));
                    track.Height = BE.U32(b, p + (v1 ? 92 : 80));
                    break;
                }
                case "edts":
                    foreach (var e in Boxes.InBuffer(b, box.PayloadStart, box.End))
                    {
                        if (e.Type != "elst") continue;
                        int p = e.PayloadStart;
                        bool v1 = b[p] == 1;
                        uint count = BE.U32(b, p + 4);
                        p += 8;
                        for (uint i = 0; i < count; i++)
                        {
                            ulong dur = v1 ? BE.U64(b, p) : BE.U32(b, p);
                            long mediaTime = v1 ? (long)BE.U64(b, p + 8) : (int)BE.U32(b, p + 4);
                            p += v1 ? 16 : 8;
                            track.Edits.Add(new Mp4Edit(dur, mediaTime, (short)BE.U16(b, p), (short)BE.U16(b, p + 2)));
                            p += 4;
                        }
                    }
                    break;
                case "mdia":
                    ParseMdia(b, box, track, flat);
                    break;
            }
        }
        return trackId;
    }

    private static void ParseMdia(byte[] b, Boxes.Box mdia, Mp4Track track, FlatTables flat)
    {
        foreach (var box in Boxes.InBuffer(b, mdia.PayloadStart, mdia.End))
        {
            switch (box.Type)
            {
                case "mdhd":
                {
                    int p = box.PayloadStart;
                    bool v1 = b[p] == 1;
                    track.Timescale = BE.U32(b, p + (v1 ? 20 : 12));
                    track.Language = BE.U16(b, p + (v1 ? 32 : 20));
                    break;
                }
                case "hdlr":
                    track.Handler = Encoding.ASCII.GetString(b, box.PayloadStart + 8, 4);
                    break;
                case "minf":
                    foreach (var m in Boxes.InBuffer(b, box.PayloadStart, box.End))
                    {
                        if (m.Type != "stbl") continue;
                        foreach (var s in Boxes.InBuffer(b, m.PayloadStart, m.End))
                        {
                            if (s.Type == "stsd")
                                track.SampleDescriptions = b.AsSpan(s.Position, s.Size).ToArray();
                            else
                                flat.Read(b, s);
                        }
                    }
                    break;
            }
        }
    }

    private static void ParseMoof(byte[] b, long moofFilePos, Mp4Track track, uint trackId, TrackExtends trex)
    {
        // Data offsets are file positions measured from the start of the moof box.
        long previousTrafDataEnd = moofFilePos;

        foreach (var traf in Boxes.InBuffer(b, 0, b.Length))
        {
            if (traf.Type != "traf") continue;

            uint tfhdFlags = 0, descIndex = trex.DescriptionIndex;
            uint defDuration = trex.Duration, defSize = trex.Size, defFlags = trex.Flags;
            long baseOffset = previousTrafDataEnd;
            bool thisTrack = true;
            ulong? decodeTime = null;

            foreach (var box in Boxes.InBuffer(b, traf.PayloadStart, traf.End))
            {
                if (box.Type == "tfhd")
                {
                    int p = box.PayloadStart;
                    tfhdFlags = BE.U32(b, p) & 0xFFFFFF;
                    thisTrack = BE.U32(b, p + 4) == trackId;
                    p += 8;
                    if ((tfhdFlags & 0x000001) != 0) { baseOffset = (long)BE.U64(b, p); p += 8; }
                    else if ((tfhdFlags & 0x020000) != 0) baseOffset = moofFilePos;
                    if ((tfhdFlags & 0x000002) != 0) { descIndex = BE.U32(b, p); p += 4; }
                    if ((tfhdFlags & 0x000008) != 0) { defDuration = BE.U32(b, p); p += 4; }
                    if ((tfhdFlags & 0x000010) != 0) { defSize = BE.U32(b, p); p += 4; }
                    if ((tfhdFlags & 0x000020) != 0) { defFlags = BE.U32(b, p); }
                }
                else if (box.Type == "tfdt")
                {
                    int p = box.PayloadStart;
                    decodeTime = b[p] == 1 ? BE.U64(b, p + 4) : BE.U32(b, p + 4);
                }
            }
            if (!thisTrack) continue;
            if (descIndex > 1) throw new NotSupportedException("Multiple sample descriptions are not supported");

            // A gap before this fragment (decode time ahead of where the samples so far end)
            // is folded into the previous sample's duration so timing stays aligned.
            if (decodeTime is { } dt && track.Samples.Count > 0)
            {
                ulong soFar = track.MediaDuration;
                if (dt > soFar)
                {
                    var last = track.Samples[^1];
                    last.Duration += (uint)Math.Min(dt - soFar, uint.MaxValue - last.Duration);
                    track.Samples[^1] = last;
                }
            }

            long dataPos = baseOffset;
            foreach (var box in Boxes.InBuffer(b, traf.PayloadStart, traf.End))
            {
                if (box.Type != "trun") continue;
                int p = box.PayloadStart;
                byte version = b[p];
                uint flags = BE.U32(b, p) & 0xFFFFFF;
                uint count = BE.U32(b, p + 4);
                p += 8;
                if ((flags & 0x001) != 0) { dataPos = baseOffset + (int)BE.U32(b, p); p += 4; }
                uint? firstFlags = null;
                if ((flags & 0x004) != 0) { firstFlags = BE.U32(b, p); p += 4; }

                for (uint i = 0; i < count; i++)
                {
                    uint duration = defDuration, size = defSize;
                    uint sampleFlags = i == 0 && firstFlags is { } ff ? ff : defFlags;
                    int cto = 0;
                    if ((flags & 0x100) != 0) { duration = BE.U32(b, p); p += 4; }
                    if ((flags & 0x200) != 0) { size = BE.U32(b, p); p += 4; }
                    if ((flags & 0x400) != 0) { sampleFlags = BE.U32(b, p); p += 4; }
                    if ((flags & 0x800) != 0)
                    {
                        uint raw = BE.U32(b, p);
                        cto = version == 0 ? (int)Math.Min(raw, int.MaxValue) : (int)raw;
                        p += 4;
                    }

                    track.Samples.Add(new Mp4Sample
                    {
                        Offset = dataPos,
                        Size = size,
                        Duration = duration,
                        CompositionOffset = cto,
                        IsSync = (sampleFlags & NonSyncSample) == 0,
                    });
                    dataPos += size;
                }
            }
            previousTrafDataEnd = dataPos;
        }
    }

    /// <summary>Sample tables of an ordinary (non-fragmented) MP4.</summary>
    private sealed class FlatTables
    {
        private readonly List<(uint Count, uint Delta)> _stts = new();
        private readonly List<(uint Count, int Offset)> _ctts = new();
        private readonly List<(uint FirstChunk, uint PerChunk)> _stsc = new();
        private readonly List<uint> _sizes = new();
        private readonly List<long> _chunkOffsets = new();
        private HashSet<uint>? _syncSamples;
        private uint _fixedSize, _sampleCount;

        public void Read(byte[] b, Boxes.Box box)
        {
            int p = box.PayloadStart;
            byte version = b[p];
            switch (box.Type)
            {
                case "stts":
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++)
                        _stts.Add((BE.U32(b, p + 8 + (int)i * 8), BE.U32(b, p + 12 + (int)i * 8)));
                    break;
                case "ctts":
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++)
                    {
                        uint raw = BE.U32(b, p + 12 + (int)i * 8);
                        _ctts.Add((BE.U32(b, p + 8 + (int)i * 8), version == 0 ? (int)Math.Min(raw, int.MaxValue) : (int)raw));
                    }
                    break;
                case "stsc":
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++)
                        _stsc.Add((BE.U32(b, p + 8 + (int)i * 12), BE.U32(b, p + 12 + (int)i * 12)));
                    break;
                case "stsz":
                    _fixedSize = BE.U32(b, p + 4);
                    _sampleCount = BE.U32(b, p + 8);
                    if (_fixedSize == 0)
                        for (uint i = 0; i < _sampleCount; i++) _sizes.Add(BE.U32(b, p + 12 + (int)i * 4));
                    break;
                case "stco":
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++) _chunkOffsets.Add(BE.U32(b, p + 8 + (int)i * 4));
                    break;
                case "co64":
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++) _chunkOffsets.Add((long)BE.U64(b, p + 8 + (int)i * 8));
                    break;
                case "stss":
                    _syncSamples = new HashSet<uint>();
                    for (uint i = 0, n = BE.U32(b, p + 4); i < n; i++) _syncSamples.Add(BE.U32(b, p + 8 + (int)i * 4));
                    break;
            }
        }

        public void AppendSamples(List<Mp4Sample> samples)
        {
            if (_sampleCount == 0) return; // fragmented file: the tables are empty

            var durations = Expand(_stts.Select(e => (e.Count, (long)e.Delta)), _sampleCount);
            var ctos = _ctts.Count > 0 ? Expand(_ctts.Select(e => (e.Count, (long)e.Offset)), _sampleCount) : null;

            uint sample = 0;
            for (int chunk = 0; chunk < _chunkOffsets.Count && sample < _sampleCount; chunk++)
            {
                uint perChunk = 0;
                foreach (var e in _stsc)
                    if (e.FirstChunk <= (uint)chunk + 1) perChunk = e.PerChunk;
                long offset = _chunkOffsets[chunk];
                for (uint k = 0; k < perChunk && sample < _sampleCount; k++, sample++)
                {
                    uint size = _fixedSize != 0 ? _fixedSize : _sizes[(int)sample];
                    samples.Add(new Mp4Sample
                    {
                        Offset = offset,
                        Size = size,
                        Duration = (uint)durations[(int)sample],
                        CompositionOffset = ctos is null ? 0 : (int)ctos[(int)sample],
                        IsSync = _syncSamples is null || _syncSamples.Contains(sample + 1),
                    });
                    offset += size;
                }
            }
        }

        private static long[] Expand(IEnumerable<(uint Count, long Value)> runs, uint total)
        {
            var result = new long[total];
            int i = 0;
            foreach (var (count, value) in runs)
                for (uint k = 0; k < count && i < total; k++) result[i++] = value;
            return result;
        }
    }
}

/// <summary>ISO base media box walking, over a file stream or an in-memory buffer.</summary>
internal static class Boxes
{
    internal readonly record struct Box(string Type, int Position, int Size, int HeaderSize)
    {
        public int PayloadStart => Position + HeaderSize;
        public int End => Position + Size;
    }

    internal readonly record struct StreamBox(string Type, long Position, long Size, int HeaderSize);

    public static IEnumerable<StreamBox> InStream(Stream s, long start, long end)
    {
        var buf = new byte[8];
        long pos = start;
        while (pos + 8 <= end)
        {
            s.Position = pos;
            s.ReadExactly(buf, 0, 8);
            long size = BE.U32(buf, 0);
            string type = Encoding.ASCII.GetString(buf, 4, 4);
            int header = 8;
            if (size == 1)
            {
                s.ReadExactly(buf, 0, 8);
                size = (long)BE.U64(buf, 0);
                header = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }
            if (size < header || pos + size > end)
                throw new InvalidDataException($"Bad box '{type}' at {pos}");
            yield return new StreamBox(type, pos, size, header);
            pos += size;
        }
    }

    public static byte[] ReadPayload(Stream s, StreamBox box)
    {
        long length = box.Size - box.HeaderSize;
        if (length > 512L * 1024 * 1024) throw new InvalidDataException($"'{box.Type}' box too large");
        var data = new byte[length];
        s.Position = box.Position + box.HeaderSize;
        s.ReadExactly(data, 0, data.Length);
        return data;
    }

    public static IEnumerable<Box> InBuffer(byte[] b, int start, int end)
    {
        int pos = start;
        while (pos + 8 <= end)
        {
            long size = BE.U32(b, pos);
            string type = Encoding.ASCII.GetString(b, pos + 4, 4);
            int header = 8;
            if (size == 1) { size = (long)BE.U64(b, pos + 8); header = 16; }
            else if (size == 0) size = end - pos;
            if (size < header || pos + size > end)
                throw new InvalidDataException($"Bad box '{type}' at {pos}");
            yield return new Box(type, pos, (int)size, header);
            pos += (int)size;
        }
    }
}

/// <summary>Big-endian reads.</summary>
internal static class BE
{
    public static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(p, 2));
    public static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p, 4));
    public static ulong U64(byte[] b, int p) => BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(p, 8));
}
