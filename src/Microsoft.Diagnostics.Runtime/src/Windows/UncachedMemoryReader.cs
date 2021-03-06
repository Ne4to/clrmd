﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.IO;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    internal sealed class UncachedMemoryReader : MinidumpMemoryReader
    {
        private readonly ImmutableArray<MinidumpSegment> _segments;
        private readonly Stream _stream;
        private readonly object _sync = new object();
        private readonly bool _leaveOpen;

        public UncachedMemoryReader(ImmutableArray<MinidumpSegment> segments, Stream stream, int pointerSize, bool leaveOpen)
        {
            _segments = segments;
            _stream = stream;
            PointerSize = pointerSize;
            _leaveOpen = leaveOpen;
        }

        public override void Dispose()
        {
            if (!_leaveOpen)
                _stream.Dispose();
        }

        public override int PointerSize { get; }

        public override int ReadFromRva(ulong rva, Span<byte> buffer)
        {
            // todo: test bounds
            lock (_sync)
            {
                if ((ulong)_stream.Length <= rva)
                    return 0;

                _stream.Position = (long)rva;
                return _stream.Read(buffer);
            }
        }

        public override int Read(ulong address, Span<byte> buffer)
        {
            if (address == 0)
                return 0;

            lock (_sync)
            {
                try
                {
                    int bytesRead = 0;

                    while (bytesRead < buffer.Length)
                    {
                        ulong currAddress = address + (uint)bytesRead;
                        int curr = GetSegmentContaining(currAddress);
                        if (curr == -1)
                            break;

                        MinidumpSegment seg = _segments[curr];
                        ulong offset = currAddress - seg.VirtualAddress;

                        Span<byte> slice = buffer.Slice(bytesRead, Math.Min(buffer.Length - bytesRead, (int)(seg.Size - offset)));
                        _stream.Position = (long)(seg.FileOffset + offset);
                        int read = _stream.Read(slice);
                        if (read == 0)
                            break;

                        bytesRead += read;
                    }

                    return bytesRead;
                }
                catch (IOException)
                {
                    return 0;
                }
            }
        }

        private int GetSegmentContaining(ulong address)
        {
            int result = -1;
            int lower = 0;
            int upper = _segments.Length - 1;

            while (lower <= upper)
            {
                int mid = (lower + upper) >> 1;
                MinidumpSegment seg = _segments[mid];

                if (seg.Contains(address))
                {
                    result = mid;
                    break;
                }

                if (address < seg.VirtualAddress)
                    upper = mid - 1;
                else
                    lower = mid + 1;
            }

            return result;
        }
    }
}
