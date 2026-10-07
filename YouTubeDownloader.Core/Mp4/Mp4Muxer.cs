using System.Buffers.Binary;
using System.Text;

namespace YouTubeDownloader.Core.Mp4;

/// <summary>
/// Combines a video-only MP4 and an audio-only MP4 (YouTube serves them separately, as
/// fragmented DASH files) into one ordinary MP4 with both tracks. Pure C# with no ffmpeg, so
/// it runs on phones. Samples are copied byte for byte; nothing is re-encoded.
/// </summary>
public static class Mp4Muxer
{
    private const uint MovieTimescale = 1000;
    private const double ChunkSeconds = 1.0;

    /// <summary>Muxes on a worker thread. Progress is the share of media bytes copied, 0 to 1.</summary>
    public static Task MuxAsync(
        string videoPath, string audioPath, string outputPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Mux(videoPath, audioPath, outputPath, progress, ct), ct);

    public static void Mux(
        string videoPath, string audioPath, string outputPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var video = Mp4TrackReader.Read(videoPath);
        var audio = Mp4TrackReader.Read(audioPath);
        if (video.Handler != "vide")
            throw new InvalidDataException($"Expected a video track, found '{video.Handler}'.");
        if (audio.Handler != "soun")
            throw new InvalidDataException($"Expected an audio track, found '{audio.Handler}'.");

        try
        {
            Write(outputPath, new[] { video, audio }, progress, ct);
        }
        catch
        {
            try { File.Delete(outputPath); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>A run of consecutive samples of one track, copied as one block.</summary>
    private sealed class Chunk
    {
        public int Track;
        public int SampleCount;
        public long SourceOffset;
        public long Length;
        public double StartSeconds;
        public long OutputOffset;
    }

    private static void Write(string outputPath, Mp4Track[] tracks, IProgress<double>? progress, CancellationToken ct)
    {
        var chunks = BuildChunks(tracks);
        byte[] ftyp = BuildFtyp();

        // co64 entries are fixed width, so the moov size doesn't depend on the offsets in it:
        // build once to learn where the media data starts, then again with real offsets.
        byte[] moov = BuildMoov(tracks, chunks);
        long dataStart = ftyp.Length + moov.Length + 16; // 16 = mdat header with 64-bit size
        long position = dataStart;
        foreach (var c in chunks)
        {
            c.OutputOffset = position;
            position += c.Length;
        }
        long dataLength = position - dataStart;
        moov = BuildMoov(tracks, chunks);
        if (ftyp.Length + moov.Length + 16 != dataStart)
            throw new InvalidOperationException("moov size changed between passes");

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        output.Write(ftyp);
        output.Write(moov);
        Span<byte> mdat = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(mdat, 1);
        Encoding.ASCII.GetBytes("mdat", mdat.Slice(4, 4));
        BinaryPrimitives.WriteUInt64BigEndian(mdat.Slice(8), (ulong)(16 + dataLength));
        output.Write(mdat);

        var sources = tracks
            .Select(t => new FileStream(t.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            .ToArray();
        try
        {
            var buffer = new byte[1 << 20];
            long copied = 0;
            double reported = -1;
            foreach (var c in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var source = sources[c.Track];
                source.Position = c.SourceOffset;
                long left = c.Length;
                while (left > 0)
                {
                    int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (n <= 0) throw new EndOfStreamException($"{Path.GetFileName(tracks[c.Track].SourcePath)} ended early");
                    output.Write(buffer, 0, n);
                    left -= n;
                    copied += n;
                }

                double fraction = dataLength == 0 ? 1 : (double)copied / dataLength;
                if (progress != null && fraction - reported >= 0.005)
                {
                    reported = fraction;
                    progress.Report(fraction);
                }
            }
            progress?.Report(1);
        }
        finally
        {
            foreach (var s in sources) s.Dispose();
        }
    }

    private static List<Chunk> BuildChunks(Mp4Track[] tracks)
    {
        var all = new List<Chunk>();
        for (int t = 0; t < tracks.Length; t++)
        {
            var track = tracks[t];
            var samples = track.Samples;
            ulong limit = Math.Max(1UL, (ulong)(track.Timescale * ChunkSeconds));
            ulong decode = 0;
            int i = 0;
            while (i < samples.Count)
            {
                var chunk = new Chunk
                {
                    Track = t,
                    SourceOffset = samples[i].Offset,
                    StartSeconds = (double)decode / track.Timescale,
                };
                ulong chunkStart = decode;
                do
                {
                    chunk.Length += samples[i].Size;
                    chunk.SampleCount++;
                    decode += samples[i].Duration;
                    i++;
                }
                while (i < samples.Count
                       && samples[i].Offset == chunk.SourceOffset + chunk.Length
                       && decode - chunkStart < limit);
                all.Add(chunk);
            }
        }

        // Interleave by time so a player never has to seek far between the two tracks.
        // OrderBy is stable, so each track's chunks stay in sample order.
        return all.OrderBy(c => c.StartSeconds).ThenBy(c => c.Track).ToList();
    }

    private static byte[] BuildFtyp()
    {
        var w = new BoxWriter();
        w.Begin("ftyp");
        w.Ascii("isom");
        w.U32(0x200);
        foreach (var brand in new[] { "isom", "iso2", "avc1", "mp41" }) w.Ascii(brand);
        w.End();
        return w.ToArray();
    }

    private static byte[] BuildMoov(Mp4Track[] tracks, List<Chunk> chunks)
    {
        var edits = tracks.Select(OutputEdits).ToArray();
        var durations = new ulong[tracks.Length];
        for (int t = 0; t < tracks.Length; t++)
        {
            durations[t] = edits[t].Count > 0
                ? edits[t].Aggregate(0UL, (sum, e) => sum + e.SegmentDuration)
                : Scale(tracks[t].MediaDuration, tracks[t].Timescale, MovieTimescale);
        }
        ulong movieDuration = durations.Max();

        var w = new BoxWriter();
        w.Begin("moov");

        bool v1 = movieDuration > uint.MaxValue;
        w.BeginFull("mvhd", v1 ? (byte)1 : (byte)0, 0);
        if (v1) { w.U64(0); w.U64(0); w.U32(MovieTimescale); w.U64(movieDuration); }
        else { w.U32(0); w.U32(0); w.U32(MovieTimescale); w.U32((uint)movieDuration); }
        w.U32(0x00010000); // rate 1.0
        w.U16(0x0100);     // volume 1.0
        w.Zeros(2 + 8);
        w.Matrix();
        w.Zeros(24);
        w.U32((uint)tracks.Length + 1); // next track id
        w.End();

        for (int t = 0; t < tracks.Length; t++)
        {
            var trackChunks = chunks.Where(c => c.Track == t).ToList();
            WriteTrak(w, (uint)t + 1, tracks[t], edits[t], durations[t], trackChunks);
        }

        w.End();
        return w.ToArray();
    }

    /// <summary>The source edit list converted to the output movie timescale.</summary>
    private static List<Mp4Edit> OutputEdits(Mp4Track track)
    {
        var result = new List<Mp4Edit>();
        uint sourceMovieScale = track.MovieTimescale != 0 ? track.MovieTimescale : track.Timescale;

        // When the last frame stops being shown, in media time: decode time plus the
        // composition (B-frame) offset plus the frame's own duration.
        ulong decode = 0;
        long lastShown = 0;
        foreach (var s in track.Samples)
        {
            lastShown = Math.Max(lastShown, (long)decode + s.CompositionOffset + s.Duration);
            decode += s.Duration;
        }

        foreach (var e in track.Edits)
        {
            if (e.MediaTime < 0)
            {
                // Empty edit: a delay before the track starts.
                result.Add(e with { SegmentDuration = Scale(e.SegmentDuration, sourceMovieScale, MovieTimescale) });
                continue;
            }

            // Show from MediaTime until the last frame ends. The edit's MediaTime usually
            // cancels the first frame's composition offset, so this is the full track length.
            ulong shown = lastShown > e.MediaTime ? (ulong)(lastShown - e.MediaTime) : 0;
            result.Add(e with { SegmentDuration = Scale(shown, track.Timescale, MovieTimescale) });
        }
        return result;
    }

    private static void WriteTrak(BoxWriter w, uint id, Mp4Track track, List<Mp4Edit> edits, ulong duration, List<Chunk> chunks)
    {
        bool isAudio = track.Handler == "soun";
        var samples = track.Samples;

        w.Begin("trak");

        bool tkhdV1 = duration > uint.MaxValue;
        w.BeginFull("tkhd", tkhdV1 ? (byte)1 : (byte)0, 0x000003); // enabled, in movie
        if (tkhdV1) { w.U64(0); w.U64(0); w.U32(id); w.U32(0); w.U64(duration); }
        else { w.U32(0); w.U32(0); w.U32(id); w.U32(0); w.U32((uint)duration); }
        w.Zeros(8);
        w.U16(0); // layer
        w.U16(0); // alternate group
        w.U16(isAudio ? (ushort)0x0100 : (ushort)0);
        w.Zeros(2);
        w.Matrix();
        w.U32(isAudio ? 0 : track.Width);
        w.U32(isAudio ? 0 : track.Height);
        w.End();

        if (edits.Count > 0)
        {
            bool elstV1 = edits.Any(e => e.SegmentDuration > uint.MaxValue || e.MediaTime > int.MaxValue);
            w.Begin("edts");
            w.BeginFull("elst", elstV1 ? (byte)1 : (byte)0, 0);
            w.U32((uint)edits.Count);
            foreach (var e in edits)
            {
                if (elstV1) { w.U64(e.SegmentDuration); w.U64((ulong)e.MediaTime); }
                else { w.U32((uint)e.SegmentDuration); w.U32((uint)(int)e.MediaTime); }
                w.U16((ushort)e.RateInteger);
                w.U16((ushort)e.RateFraction);
            }
            w.End();
            w.End();
        }

        w.Begin("mdia");

        ulong mediaDuration = track.MediaDuration;
        bool mdhdV1 = mediaDuration > uint.MaxValue;
        w.BeginFull("mdhd", mdhdV1 ? (byte)1 : (byte)0, 0);
        if (mdhdV1) { w.U64(0); w.U64(0); w.U32(track.Timescale); w.U64(mediaDuration); }
        else { w.U32(0); w.U32(0); w.U32(track.Timescale); w.U32((uint)mediaDuration); }
        w.U16(track.Language);
        w.U16(0);
        w.End();

        w.BeginFull("hdlr", 0, 0);
        w.U32(0);
        w.Ascii(track.Handler);
        w.Zeros(12);
        w.Bytes(Encoding.UTF8.GetBytes(isAudio ? "SoundHandler\0" : "VideoHandler\0"));
        w.End();

        w.Begin("minf");
        if (isAudio)
        {
            w.BeginFull("smhd", 0, 0);
            w.U16(0); // balance
            w.U16(0);
            w.End();
        }
        else
        {
            w.BeginFull("vmhd", 0, 1);
            w.U16(0); // graphics mode
            w.Zeros(6);
            w.End();
        }

        w.Begin("dinf");
        w.BeginFull("dref", 0, 0);
        w.U32(1);
        w.BeginFull("url ", 0, 1); // media is in this file
        w.End();
        w.End();
        w.End();

        w.Begin("stbl");
        w.Bytes(track.SampleDescriptions);

        // stts: decode durations, run-length encoded.
        var stts = RunLength(samples.Select(s => s.Duration));
        w.BeginFull("stts", 0, 0);
        w.U32((uint)stts.Count);
        foreach (var (count, value) in stts) { w.U32(count); w.U32(value); }
        w.End();

        // ctts: composition offsets (B-frames), only when there are any.
        if (samples.Any(s => s.CompositionOffset != 0))
        {
            bool signed = samples.Any(s => s.CompositionOffset < 0);
            var ctts = RunLength(samples.Select(s => s.CompositionOffset));
            w.BeginFull("ctts", signed ? (byte)1 : (byte)0, 0);
            w.U32((uint)ctts.Count);
            foreach (var (count, value) in ctts) { w.U32(count); w.U32((uint)value); }
            w.End();
        }

        // stss: keyframes. Absent means every sample is one.
        if (!isAudio && samples.Any(s => !s.IsSync))
        {
            var sync = new List<uint>();
            for (int i = 0; i < samples.Count; i++)
                if (samples[i].IsSync) sync.Add((uint)i + 1);
            w.BeginFull("stss", 0, 0);
            w.U32((uint)sync.Count);
            foreach (uint n in sync) w.U32(n);
            w.End();
        }

        // stsc: samples per chunk, run-length encoded by chunk number.
        var stsc = new List<(uint FirstChunk, uint PerChunk)>();
        for (int c = 0; c < chunks.Count; c++)
        {
            uint per = (uint)chunks[c].SampleCount;
            if (stsc.Count == 0 || stsc[^1].PerChunk != per) stsc.Add(((uint)c + 1, per));
        }
        w.BeginFull("stsc", 0, 0);
        w.U32((uint)stsc.Count);
        foreach (var (first, per) in stsc) { w.U32(first); w.U32(per); w.U32(1); }
        w.End();

        // stsz: sample sizes, or one shared size.
        uint firstSize = samples[0].Size;
        bool sameSize = samples.All(s => s.Size == firstSize);
        w.BeginFull("stsz", 0, 0);
        w.U32(sameSize ? firstSize : 0);
        w.U32((uint)samples.Count);
        if (!sameSize) foreach (var s in samples) w.U32(s.Size);
        w.End();

        // co64: where each chunk starts in the output file.
        w.BeginFull("co64", 0, 0);
        w.U32((uint)chunks.Count);
        foreach (var c in chunks) w.U64((ulong)c.OutputOffset);
        w.End();

        w.End(); // stbl
        w.End(); // minf
        w.End(); // mdia
        w.End(); // trak
    }

    private static List<(uint Count, T Value)> RunLength<T>(IEnumerable<T> values) where T : IEquatable<T>
    {
        var runs = new List<(uint Count, T Value)>();
        foreach (var v in values)
        {
            if (runs.Count > 0 && runs[^1].Value.Equals(v))
                runs[^1] = (runs[^1].Count + 1, v);
            else
                runs.Add((1, v));
        }
        return runs;
    }

    private static ulong Scale(ulong value, uint from, uint to) =>
        from == 0 ? 0 : (ulong)Math.Round((decimal)value * to / from);

    /// <summary>Builds boxes in memory, patching each box's size when it is closed.</summary>
    private sealed class BoxWriter
    {
        private readonly MemoryStream _ms = new();
        private readonly Stack<long> _open = new();
        private readonly byte[] _scratch = new byte[8];

        public void Begin(string type)
        {
            _open.Push(_ms.Position);
            U32(0);
            Ascii(type);
        }

        public void BeginFull(string type, byte version, uint flags)
        {
            Begin(type);
            U32(((uint)version << 24) | (flags & 0xFFFFFF));
        }

        public void End()
        {
            long start = _open.Pop();
            long size = _ms.Position - start;
            if (size > uint.MaxValue) throw new InvalidOperationException("box too large");
            long here = _ms.Position;
            _ms.Position = start;
            U32((uint)size);
            _ms.Position = here;
        }

        public void U16(ushort v) { BinaryPrimitives.WriteUInt16BigEndian(_scratch, v); _ms.Write(_scratch, 0, 2); }
        public void U32(uint v) { BinaryPrimitives.WriteUInt32BigEndian(_scratch, v); _ms.Write(_scratch, 0, 4); }
        public void U64(ulong v) { BinaryPrimitives.WriteUInt64BigEndian(_scratch, v); _ms.Write(_scratch, 0, 8); }
        public void Bytes(byte[] b) => _ms.Write(b, 0, b.Length);
        public void Ascii(string s) => Bytes(Encoding.ASCII.GetBytes(s));
        public void Zeros(int n) { for (int i = 0; i < n; i++) _ms.WriteByte(0); }

        public void Matrix()
        {
            foreach (uint v in new uint[] { 0x00010000, 0, 0, 0, 0x00010000, 0, 0, 0, 0x40000000 }) U32(v);
        }

        public byte[] ToArray() => _ms.ToArray();
    }
}
