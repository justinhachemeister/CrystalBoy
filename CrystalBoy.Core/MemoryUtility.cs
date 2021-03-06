﻿using System;
using System.IO;

namespace CrystalBoy.Core
{
	public static class MemoryUtility
	{
		private const int BufferLength = 16384;

		[ThreadStatic]
		private static byte[] _buffer;

		private static byte[] Buffer => _buffer ?? (_buffer = new byte[BufferLength]);

		#region Reading

		public static MemoryBlock ReadFile(FileInfo fileInfo, bool roundToPowerOfTwo)
		{
			FileStream fileStream;

			// Open the file in exclusive mode
			using (fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				return ReadStream
				(
					fileStream,
					checked
					(
						roundToPowerOfTwo ?
							(int)RoundToPowerOfTwo((ulong)fileStream.Length) :
							(int)fileStream.Length
					)
				);
			}
		}

		private static ulong RoundToPowerOfTwo(ulong value)
		{
			// We can't do our job if the value is too big. 
			if (value > 0x8000000000000000UL) throw new ArgumentOutOfRangeException(nameof(value));

			// Only do the "complex" bit arithmetic if the number is not already a power of two.
			// We can consider 0 to be a power of two here… Anyway, loading an empty file is not very interresting.
			if ((value & (value - 1)) != 0)
			{
				// We'll find the enclosing power of two in a maximum of 14 compares & 15 shifts for 63 bit values, and a maximum of 11 compares & 10 shifts for 31 bit values.
				// This may not be the best algorithm out there, but it should at least be readable.
				// The loops have been unrolled, to avoid unnecessary operations.

				ulong enclosing = 0x80;

				// Find the appropriate byte (minimum 1 compare & 0 shift, maximum 7 compares & 8 shifts)
				if
				(
					enclosing < value &&
					(enclosing <<= 8) < value &&
					(enclosing <<= 8) < value &&
					(enclosing <<= 8) < value &&
					(enclosing <<= 8) < value &&
					(enclosing <<= 8) < value &&
					(enclosing <<= 8) < value
				)
				{
					enclosing <<= 8;
				}

				// Find the exactly greater value (minimum 1 compare & 1 shift, maximum 7 compares & 7 shifts)
				ulong nextEnclosing;

				if ((nextEnclosing = enclosing >> 1) <= value) return enclosing;
				if ((nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value) return enclosing;
				if ((nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value) return enclosing;
				if ((nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value) return enclosing;
				if ((nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value) return enclosing;
				if ((nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value) return enclosing;

				return (nextEnclosing = (enclosing = nextEnclosing) >> 1) <= value ?
					enclosing :
					nextEnclosing;
			}

			return value;
		}

		public static MemoryBlock ReadStream(Stream stream, int length)
		{
			MemoryBlock memoryBlock;

			// Initialize variables
			memoryBlock = null;

			try
			{
				// Create the memory block once the file has been successfully opened
				memoryBlock = new MemoryBlock(length);

				// Read the file using the extension method defined below
				stream.Read(memoryBlock, 0, memoryBlock.Length);
			}
			catch when (memoryBlock != null)
			{
				// Dispose the memory if needed
				memoryBlock.Dispose();

				throw;
			}
			finally
			{
				// Close the file
				stream.Close();
			}

			return memoryBlock;
		}

		public static unsafe int Read(this Stream stream, MemoryBlock memoryBlock, int offset, int length)
		{
			byte* pMemory;
			int bytesRead, bytesToRead, totalBytesRead;
			byte[] buffer;

			if (memoryBlock == null)
				throw new ArgumentNullException();
			if (offset >= memoryBlock.Length && length != 0)
				throw new ArgumentOutOfRangeException("offset");
			if (length < 0 || offset + length > memoryBlock.Length)
				throw new ArgumentOutOfRangeException("length");

			// Get a pointer to the memory block
			pMemory = (byte*)memoryBlock.Pointer + offset;

			// Obtain a reference to the buffer (lazy allocation)
			buffer = Buffer;

			totalBytesRead = 0;
			bytesToRead = Math.Min(BufferLength, length);

			// Read the file in chunks
			fixed (byte* pBuffer = buffer)
			{
				while ((bytesRead = stream.Read(buffer, 0, bytesToRead)) > 0)
				{
					MemoryBlock.Copy(pMemory, pBuffer, bytesRead);
					totalBytesRead += bytesRead;
					pMemory += bytesRead;
					length -= bytesRead;
					if (length < bytesToRead)
						bytesToRead = length;
				}
			}

			return totalBytesRead;
		}

		public static unsafe int Read(this BinaryReader reader, MemoryBlock memoryBlock, int offset, int length)
		{
			byte* pMemory;
			int bytesRead, bytesToRead, totalBytesRead;
			byte[] buffer;

			if (memoryBlock == null)
				throw new ArgumentNullException();
			if (offset >= memoryBlock.Length && length != 0)
				throw new ArgumentOutOfRangeException("offset");
			if (length < 0 || offset + length > memoryBlock.Length)
				throw new ArgumentOutOfRangeException("length");

			// Get a pointer to the memory block
			pMemory = (byte*)memoryBlock.Pointer;

			// Obtain a reference to the buffer (lazy allocation)
			buffer = Buffer;

			totalBytesRead = 0;
			bytesToRead = Math.Min(BufferLength, length);

			// Read the file in chunks
			fixed (byte* pBuffer = buffer)
			{
				while ((bytesRead = reader.Read(buffer, 0, bytesToRead)) > 0)
				{
					Memory.Copy(pMemory, pBuffer, (uint)bytesRead);
					totalBytesRead += bytesRead;
					pMemory += bytesRead;
					length -= bytesRead;
					if (length < bytesToRead)
						bytesToRead = length;
				}
			}

			return totalBytesRead;
		}

		#endregion

		#region Writing

		public static unsafe void WriteFile(FileInfo fileInfo, MemoryBlock memoryBlock)
		{
			FileStream fileStream;

			if (fileInfo == null)
				throw new ArgumentNullException("fileInfo");
			if (memoryBlock == null)
				throw new ArgumentNullException("memoryBlock");

			// Open the file in exclusive mode
			using (fileStream = fileInfo.Open(FileMode.Open, FileAccess.Write, FileShare.Read))
				fileStream.Write(memoryBlock, 0, memoryBlock.Length);
		}

		public static unsafe void Write(this Stream stream, MemoryBlock memoryBlock, int offset, int length)
		{
			byte* pMemory;
			int bytesLeft,
				bytesToWrite;
			byte[] buffer;

			if (memoryBlock == null)
				throw new ArgumentNullException("memoryBlock");
			if (offset >= memoryBlock.Length && length != 0)
				throw new ArgumentOutOfRangeException("offset");
			if (length < 0 || offset + length > memoryBlock.Length)
				throw new ArgumentOutOfRangeException("length");

			// Initialize variables
			bytesLeft = length;
			bytesToWrite = BufferLength;

			// Get a pointer to the memory block
			pMemory = (byte*)memoryBlock.Pointer + offset;

			// Obtain a reference to the buffer (lazy allocation)
			buffer = Buffer;

			// Write the file in chunks
			fixed (byte* pBuffer = buffer)
			{
				while (bytesLeft > 0)
				{
					if (bytesLeft < bytesToWrite)
						bytesToWrite = bytesLeft;
					Memory.Copy(pBuffer, pMemory, (uint)bytesToWrite);
					stream.Write(buffer, 0, bytesToWrite);
					pMemory += bytesToWrite;
					bytesLeft -= bytesToWrite;
				}
			}
		}

		public static unsafe void Write(this BinaryWriter writer, MemoryBlock memoryBlock, int offset, int length)
		{
			byte* pMemory;
			int bytesLeft,
				bytesToWrite;
			byte[] buffer;

			if (memoryBlock == null)
				throw new ArgumentNullException("memoryBlock");
			if (offset >= memoryBlock.Length && length != 0)
				throw new ArgumentOutOfRangeException("offset");
			if (length < 0 || offset + length > memoryBlock.Length)
				throw new ArgumentOutOfRangeException("length");

			// Initialize variables
			bytesLeft = length;
			bytesToWrite = BufferLength;

			// Get a pointer to the memory block
			pMemory = (byte*)memoryBlock.Pointer + offset;

			// Obtain a reference to the buffer (lazy allocation)
			buffer = Buffer;

			// Write the file in chunks
			fixed (byte* pBuffer = buffer)
			{
				while (bytesLeft > 0)
				{
					if (bytesLeft < bytesToWrite)
						bytesToWrite = bytesLeft;
					Memory.Copy(pBuffer, pMemory, (uint)bytesToWrite);
					writer.Write(buffer, 0, bytesToWrite);
					pMemory += bytesToWrite;
					bytesLeft -= bytesToWrite;
				}
			}
		}

		#endregion
	}
}
