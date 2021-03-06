﻿using Genome;
using Intervals;
using ReferenceSequence.IO;

namespace ReferenceSequence.Common
{
    public sealed class Sequence : ISequence
    {
        public int Length { get; private set; }
        public Band[] CytogeneticBands { get; private set; }
        public GenomeAssembly Assembly { get; set; }
        
        private int _sequenceOffset;
        private byte[] _buffer;
        private char[] _decompressBuffer;

        private IIntervalSearch<MaskedEntry> _maskedIntervalSearch;
        
        private readonly char[] _convertNumberToBase;
        private bool _useNSequence;

        public Sequence()
        {
            const string bases   = "GCTA";
            _convertNumberToBase = bases.ToCharArray();
            _decompressBuffer    = new char[1024];
        }

        private static (int BaseIndex, int Shift) GetBaseIndexAndShift(int referencePosition)
        {
            int refPos    = referencePosition + 1;
            var baseIndex = (int)(refPos / 4.0);
            int shift     = (3 - refPos % 4) * 2;
            return (baseIndex, shift);
        }

        internal static int GetNumBufferBytes(int numBases) =>
            (int)((double)numBases / ReferenceSequenceCommon.NumBasesPerByte + 1);

        public void EnableNSequence() => _useNSequence = true;
        
        internal void Set(int length, int sequenceOffset, byte[] twoBitBuffer,
            IntervalArray<MaskedEntry> maskedEntryIntervalArray, Band[] cytogeneticBands)
        {
            Length                = length;
            _buffer               = twoBitBuffer;
            _maskedIntervalSearch = maskedEntryIntervalArray;
            _sequenceOffset       = sequenceOffset;
            CytogeneticBands      = cytogeneticBands;
            _useNSequence         = false;
        }

        public string Substring(int offset, int length)
        {
            if (_useNSequence) return new string('N', length);

            offset -= _sequenceOffset;

            // handle negative offsets and lengths
            if (offset < 0 || length < 1 || offset >= Length) return null;

            // sanity check: avoid going past the end of the sequence
            if (offset + length > Length) length = Length - offset;

            // allocate more memory if needed
            if (length > _decompressBuffer.Length) _decompressBuffer = new char[length];

            // set the initial state of the buffer
            (int bufferIndex, int bufferShift) = GetBaseIndexAndShift(offset - 1);
            byte currentBufferSeed = _buffer[bufferIndex];

            // get the overlapping masked interval
            MaskedEntry[] maskedEntries = _maskedIntervalSearch.GetAllOverlappingValues(offset, offset + length - 1);

            // get the first masked interval
            var  currentOffset      = 0;
            bool hasMaskedIntervals = maskedEntries != null;
            int  numIntervals       = maskedEntries?.Length ?? 0;
            var  currentMaskedEntry = hasMaskedIntervals ? maskedEntries[0] : null;

            for (var baseIndex = 0; baseIndex < length; baseIndex++)
            {
                int currentPosition = offset + baseIndex;

                if (hasMaskedIntervals && currentPosition >= currentMaskedEntry.Begin && currentPosition <= currentMaskedEntry.End)
                {
                    int numMaskedBases = MaskBases(offset, length, baseIndex, currentMaskedEntry);
                    baseIndex += numMaskedBases - 1;

                    (bufferIndex, bufferShift) = GetBaseIndexAndShift(offset + baseIndex);
                    currentBufferSeed = _buffer[bufferIndex];

                    currentOffset++;
                    hasMaskedIntervals = currentOffset < numIntervals;
                    currentMaskedEntry    = hasMaskedIntervals ? maskedEntries[currentOffset] : null;

                    continue;
                }

                // evaluate normal bases
                _decompressBuffer[baseIndex] = _convertNumberToBase[(currentBufferSeed >> bufferShift) & 3];

                bufferShift -= 2;

                if (bufferShift < 0)
                {
                    bufferShift = CompressedSequenceReader.MaxShift;
                    bufferIndex++;
                    currentBufferSeed = _buffer[bufferIndex];
                }
            }

            return new string(_decompressBuffer, 0, length);
        }

        private int MaskBases(int offset, int length, int baseIndex, MaskedEntry currentInterval)
        {
            var numBasesMasked = 0;
            for (; baseIndex <= currentInterval.End - offset && baseIndex < length; baseIndex++, numBasesMasked++)
                _decompressBuffer[baseIndex] = 'N';
            return numBasesMasked;
        }
    }
}