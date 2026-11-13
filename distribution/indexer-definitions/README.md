# Indexer definitions shipped with Cinnabarr

These are the Cardigann indexer definitions from https://github.com/Prowlarr/Indexers (`definitions/v1` to `definitions/v12`,
the highest version of each indexer), copied here so a first start works without a download. Cinnabarr downloads the current
set on its first start and then daily (`IndexerDefinitionsUrl` in the config file changes the source); these files are used only
when that download fails and nothing has been downloaded yet.

The list of indexers Prowlarr supports is at https://wiki.servarr.com/en/prowlarr/supported-indexers. The ones marked as built
into Prowlarr (not definition files) are separate code and are not in this folder.

Snapshot taken from commit 456ff1ab8f32f2f04559e953d4b8e5232bd36972 (2026-11-12).
