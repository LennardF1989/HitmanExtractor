using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LZ4;

namespace HitmanExtractor
{
    public static class Program
    {
        private static readonly List<string> FoundFileType = new List<string>();
        
        class HitmanException : Exception
        {
            public HitmanException(string message)
                : base(message)
            {
                //Do nothing
            }
        }

        class FileEntry
        {
            public ulong Hash { get; set; }
            public uint FileOffset { get; set; }
            public uint FileSize { get; set; }

            public bool IsEncrypted => (FileSize & 0x80000000) > 0;
            public bool IsCompressed => CompressedFileSize > 0;
            public uint CompressedFileSize => FileSize & 0x3fffffff;

            public string FileType { get; set; }
            public uint DecompressedFileSize { get; set; }

            public ulong FileTable1EntryOffset { get; set; }
            public uint FileTable1EntrySize { get; set; }
            public ulong FileTable2EntryOffset { get; set; }
            public uint FileTable2EntrySize { get; set; }
        }

        private const int HEADER_SIZE = 25;
        private const int BASE_FILETABLE1_ENTRY_SIZE = 20;
        private const int BASE_FILETABLE2_ENTRY_SIZE = 24;

        private static readonly byte[] _xorCypher = { 0xDC, 0x45, 0xA6, 0x9C, 0xD3, 0x72, 0x4C, 0xAB };

        public static void Main(string[] args)
        {
            try
            {
                HandleArguments(args);
            }
            catch (HitmanException ex)
            {
                Console.WriteLine(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        private static void HandleArguments(string[] args)
        {
            if (args.Length >= 2)
            {
                if (!File.Exists(args[1]))
                {
                    throw new HitmanException($"File {args[1]} not found!");
                }

                using var fileStream = new FileStream(args[1], FileMode.Open, FileAccess.Read);
                using var binaryReader = new BinaryReader(fileStream);

                Console.WriteLine("Building file entry list...");

                var fileEntryList = BuildFileEntryList(binaryReader);

                if (args[0] == "list")
                {
                    Console.WriteLine("Listing file entries...");

                    ListFileEntryList(fileEntryList, args.Skip(2).ToList());
                }
                else if (args[0] == "extract")
                {
                    Console.WriteLine("Extracting file entries...");

                    ExtractFileEntryList(binaryReader, fileEntryList, args[2], args.Skip(3).ToList());
                }
            }
            else
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("- HitmanExtractor.exe list ChunkFile [Filter ... n]");
                Console.WriteLine("- HitmanExtractor.exe extract ChunkFile OutputDirectory [Filter ... n]");
            }
        }

        private static List<FileEntry> BuildFileEntryList(BinaryReader binaryReader)
        {
            var header = Encoding.UTF8.GetString(binaryReader.ReadBytes(4));

            if (header != "2KPR")
            {
                throw new HitmanException($"ASSERT: Unknown header {header}!");
            }

            //NOTE: Skip 9 bytes
            binaryReader.BaseStream.Position += 9;

            var fileCount = binaryReader.ReadUInt32();
            var fileTable1Size = binaryReader.ReadUInt32();
            var fileTable2Size = binaryReader.ReadUInt32();

            List<FileEntry> fileEntries = new List<FileEntry>();

            for (int i = 0; i < fileCount; i++)
            {
                var fileTableOffset = binaryReader.BaseStream.Position;

                var hash = binaryReader.ReadUInt64();
                var fileOffset = binaryReader.ReadUInt32();

                //NOTE: Skip 4 bytes
                binaryReader.BaseStream.Position += 4;

                var fileSize = binaryReader.ReadUInt32();

                var fileEntry = new FileEntry
                {
                    Hash = hash,
                    FileOffset = fileOffset,
                    FileSize = fileSize,

                    FileTable1EntryOffset = (ulong)fileTableOffset,
                    FileTable1EntrySize = BASE_FILETABLE1_ENTRY_SIZE
                };

                fileEntries.Add(fileEntry);
            }

            if (binaryReader.BaseStream.Position != HEADER_SIZE + fileTable1Size)
            {
                throw new HitmanException(
                    $"ASSERT: File Table 1 ended at invalid position, expected {HEADER_SIZE + fileTable1Size}, but got {binaryReader.BaseStream.Position}"
                );
            }

            for (int i = 0; i < fileCount; i++)
            {
                var fileTableOffset = binaryReader.BaseStream.Position;

                var fileType = Encoding.UTF8.GetString(binaryReader.ReadBytes(4));
                var additionaEntrySize = binaryReader.ReadUInt32();

                //NOTE: Skip 4 bytes
                binaryReader.BaseStream.Position += 4;

                var decompressedSize = binaryReader.ReadUInt32();

                binaryReader.BaseStream.Position = fileTableOffset + BASE_FILETABLE2_ENTRY_SIZE + additionaEntrySize;

                var fileEntry = fileEntries[i];
                fileEntry.FileType = fileType;
                
                if (!FoundFileType.Contains(fileType)) FoundFileType.Add(fileType);
                
                fileEntry.DecompressedFileSize = decompressedSize;

                fileEntry.FileTable2EntryOffset = (ulong)fileTableOffset;
                fileEntry.FileTable2EntrySize = BASE_FILETABLE2_ENTRY_SIZE + additionaEntrySize;
            }

            if (binaryReader.BaseStream.Position != HEADER_SIZE + fileTable1Size + fileTable2Size)
            {
                throw new HitmanException(
                    $"ASSERT: File Table 1 ended at invalid position, expected {HEADER_SIZE + fileTable1Size + fileTable2Size}, but got {binaryReader.BaseStream.Position}"
                );
            }

            return fileEntries;
        }

        private static void ListFileEntryList(List<FileEntry> fileEntryList, List<string> filters)
        {
            foreach (var fileEntry in fileEntryList)
            {
                if (filters.Any() && !filters.Contains(fileEntry.FileType))
                {
                    continue;
                }

                Console.WriteLine(
                    $"{fileEntry.FileType} => {fileEntry.Hash:X16} @ {fileEntry.FileOffset} (IsCompressed: {fileEntry.IsCompressed} | IsEncrypted: {fileEntry.IsEncrypted} | Size: {fileEntry.CompressedFileSize} / {fileEntry.DecompressedFileSize})"
                );
            }
        }

        private static void ExtractFileEntryList(BinaryReader binaryReader, List<FileEntry> fileEntryList, string outputDirectory, List<string> filters)
        {
            foreach (var extension in FoundFileType) Directory.CreateDirectory($"{outputDirectory}/{new string(extension.ToCharArray().Reverse().ToArray())}");

            foreach (var fileEntry in fileEntryList)
            {
                if (filters.Any() && !filters.Contains(fileEntry.FileType))
                {
                    continue;
                }

                binaryReader.BaseStream.Position = fileEntry.FileOffset;

                var bytes = binaryReader.ReadBytes(
                    (int)(fileEntry.IsCompressed ? fileEntry.CompressedFileSize : fileEntry.DecompressedFileSize)
                );

                if (fileEntry.IsEncrypted)
                {
                    for (int j = 0; j < bytes.Length; j++)
                    {
                        byte currentKey = _xorCypher[j % _xorCypher.Length];

                        bytes[j] ^= currentKey;
                    }
                }

                if (fileEntry.IsCompressed)
                {
                    bytes = LZ4Codec.Decode(
                        bytes, 0, bytes.Length, (int)fileEntry.DecompressedFileSize
                    );
                }

                var fileExtension = new string(fileEntry.FileType.ToCharArray().Reverse().ToArray());

                File.WriteAllBytes(Path.Combine($"{outputDirectory}/{fileExtension}", $"{fileEntry.Hash:X16}.{fileExtension.ToLower()}"), bytes);

                Console.WriteLine(
                    $"{fileEntry.FileType} => {fileEntry.Hash:X16} @ {fileEntry.FileOffset} (IsCompressed: {fileEntry.IsCompressed} | IsEncrypted: {fileEntry.IsEncrypted} | Size: {fileEntry.CompressedFileSize} / {fileEntry.DecompressedFileSize})"
                );
            }
        }
    }
}