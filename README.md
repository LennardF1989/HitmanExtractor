# What is this?
This tool allows you to extract the chunk packages found in Hitman 3.

# How to use?
Usage is pretty straightforward, but is by no means fool-proof. All you need is a command-prompt.

## Installation
The easiest installation would be downloading the latest release from GitHub and extracting the ZIP into your `Hitman 3/Runtime`-folder.

## List contents
Run the following command:

```
HitmanExtractor.exe list ChunkFile [Filter ... n]
```

For example:

```
HitmanExtractor.exe list chunk0.rpkg ULBC PMET
```

This would list all files of the types ULBC and PMET found in chunk0.rpkg. By omitting the filters, all files are listed.

## Extract contents
Run the following command:

```
HitmanExtractor.exe extract ChunkFile OutputDirectory [Filter ... n]
```

For example:

```
HitmanExtractor.exe extract chunk0.rpkg chunk0 ULBC PMET
```

This would extract all files of the types ULBC and PMET found in chunk0.rpkg to the directory chunk0. By omitting the filters, all files are extracted.

# Notes
The Hitman 3 .rpkg fileformat has not been fully sorted out yet. In result, some assets report as 0 bytes and are not extractable. For example, this is currently the case for the types TXET and ENIL (.text and .line respectively).

The chunk packages do not actually contain any filenames. The tool will have to be extended with support for "hash to filename" mappings.