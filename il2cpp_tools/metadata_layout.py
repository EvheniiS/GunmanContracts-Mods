"""Fail clearly before interpreting an unsupported/truncated metadata layout."""
import hashlib
import struct

TABLES = ["stringLiteral", "stringLiteralData", "string", "events", "properties", "methods",
          "parameterDefaultValues", "fieldDefaultValues", "fieldAndParameterDefaultValueData", "fieldMarshaledSizes",
          "parameters", "fields", "genericParameters", "genericParameterConstraints", "genericContainers", "nestedTypes",
          "interfaces", "vtableMethods", "interfaceOffsets", "typeDefinitions", "images", "assemblies", "fieldRefs",
          "referencedAssemblies", "attributeData", "attributeDataRange", "unresolvedIndirectCallParameterTypes",
          "unresolvedIndirectCallParameterRanges", "windowsRuntimeTypeNames", "windowsRuntimeStrings", "exportedTypeDefinitions"]


def header(data):
    if len(data) < 8:
        raise ValueError('Truncated IL2CPP metadata header.')
    magic, version = struct.unpack_from('<II', data)
    if magic != 0xFAB11BAF:
        raise ValueError('Not a plain IL2CPP metadata file (bad magic); do not guess offsets.')
    if version != 31:
        raise ValueError(f'Unsupported metadata v{version}; this mapper requires v31. Update layout before research.')
    end = 8 + 8 * len(TABLES)
    if len(data) < end:
        raise ValueError('Truncated v31 metadata table header.')
    result = {name: struct.unpack_from('<II', data, 8 + i * 8) for i, name in enumerate(TABLES)}
    for name, (offset, size) in result.items():
        if size and (offset < end or offset + size > len(data)):
            raise ValueError(f'Metadata table {name} is outside file bounds; unsupported/corrupt layout.')
    for name, stride in {'typeDefinitions': 88, 'methods': 36, 'fields': 12, 'parameters': 12,
                         'images': 40, 'properties': 20, 'stringLiteral': 8}.items():
        if result[name][1] % stride:
            raise ValueError(f'Metadata {name} size is not divisible by v31 stride {stride}.')
    for name in ['string', 'typeDefinitions', 'methods', 'images']:
        if not result[name][1]:
            raise ValueError(f'Metadata table {name} is empty; mapper cannot validate this build.')
    return result


def cache_key(assembly, metadata):
    # Content hashes catch same-size/same-timestamp updates and mapper schema changes.
    return ('v31-map-3', hashlib.sha256(assembly).hexdigest(), hashlib.sha256(metadata).hexdigest())
