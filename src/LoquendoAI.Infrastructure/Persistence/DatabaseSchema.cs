namespace LoquendoAI.Infrastructure.Persistence;

/// <summary>
/// Project database migrations. The database version lives only in schema_info; the code's
/// target is ProjectManifest.CurrentSchemaVersion. V1–V6 are kept as they shipped (older projects
/// replay them in order); tables that were never used are removed by V7.
/// </summary>
internal static class DatabaseSchema
{
    /// <summary>Per-connection settings, applied on every open (WAL itself is stored in the file).</summary>
    public const string Pragmas = """
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA busy_timeout = 5000;
""";

    /// <summary>Initial schema; only run on a database without schema_info (a new project).</summary>
    public const string V1 = """
CREATE TABLE IF NOT EXISTS schema_info (
    version INTEGER NOT NULL
);

INSERT INTO schema_info(version)
SELECT 1
WHERE NOT EXISTS (SELECT 1 FROM schema_info);

CREATE TABLE IF NOT EXISTS project_info (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    schema_version INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS assets (
    id TEXT PRIMARY KEY,
    relative_path TEXT NOT NULL UNIQUE,
    kind INTEGER NOT NULL,
    sha256 TEXT NOT NULL,
    display_name TEXT NOT NULL,
    imported_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_assets_sha256 ON assets(sha256);
CREATE INDEX IF NOT EXISTS idx_assets_kind ON assets(kind);

CREATE TABLE IF NOT EXISTS asset_tags (
    asset_id TEXT NOT NULL,
    key TEXT NOT NULL,
    value TEXT NOT NULL,
    confidence REAL NOT NULL DEFAULT 1.0,
    PRIMARY KEY(asset_id, key, value),
    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_asset_tags_lookup ON asset_tags(key, value);

CREATE TABLE IF NOT EXISTS voice_profiles (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    provider_key TEXT NOT NULL,
    voice_id TEXT NOT NULL,
    rate REAL NOT NULL,
    pitch REAL NOT NULL,
    config_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS characters (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    default_voice_profile_id TEXT NULL,
    notes TEXT NULL,
    FOREIGN KEY(default_voice_profile_id) REFERENCES voice_profiles(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS episodes (
    id TEXT PRIMARY KEY,
    number INTEGER NOT NULL,
    title TEXT NOT NULL,
    synopsis TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_episodes_number ON episodes(number);

CREATE TABLE IF NOT EXISTS scenes (
    id TEXT PRIMARY KEY,
    episode_id TEXT NOT NULL,
    scene_index INTEGER NOT NULL,
    title TEXT NOT NULL,
    duration_ms INTEGER NOT NULL,
    background_asset_id TEXT NULL,
    direction_notes TEXT NULL,
    FOREIGN KEY(episode_id) REFERENCES episodes(id) ON DELETE CASCADE,
    FOREIGN KEY(background_asset_id) REFERENCES assets(id) ON DELETE SET NULL,
    UNIQUE(episode_id, scene_index)
);

CREATE TABLE IF NOT EXISTS scene_characters (
    id TEXT PRIMARY KEY,
    scene_id TEXT NOT NULL,
    character_id TEXT NOT NULL,
    sprite_asset_id TEXT NULL,
    x REAL NOT NULL,
    y REAL NOT NULL,
    scale REAL NOT NULL,
    layer INTEGER NOT NULL,
    visible INTEGER NOT NULL,
    FOREIGN KEY(scene_id) REFERENCES scenes(id) ON DELETE CASCADE,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE CASCADE,
    FOREIGN KEY(sprite_asset_id) REFERENCES assets(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS dialogue_lines (
    id TEXT PRIMARY KEY,
    scene_id TEXT NOT NULL,
    order_index INTEGER NOT NULL,
    character_id TEXT NULL,
    text TEXT NOT NULL,
    voice_profile_id TEXT NULL,
    start_offset_ms INTEGER NULL,
    duration_ms INTEGER NULL,
    FOREIGN KEY(scene_id) REFERENCES scenes(id) ON DELETE CASCADE,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE SET NULL,
    FOREIGN KEY(voice_profile_id) REFERENCES voice_profiles(id) ON DELETE SET NULL,
    UNIQUE(scene_id, order_index)
);

CREATE TABLE IF NOT EXISTS effects (
    id TEXT PRIMARY KEY,
    scene_id TEXT NOT NULL,
    preset_key TEXT NOT NULL,
    start_offset_ms INTEGER NOT NULL,
    duration_ms INTEGER NULL,
    parameters_json TEXT NOT NULL,
    FOREIGN KEY(scene_id) REFERENCES scenes(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS jobs (
    id TEXT PRIMARY KEY,
    type TEXT NOT NULL,
    status INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    progress_current INTEGER NOT NULL DEFAULT 0,
    progress_total INTEGER NOT NULL DEFAULT 0,
    error TEXT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
""";

    public const string V2 = """
CREATE TABLE IF NOT EXISTS asset_sources (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    root_path TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    last_scan_utc TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_asset_sources_root ON asset_sources(root_path);

CREATE TABLE IF NOT EXISTS folder_rules (
    id TEXT PRIMARY KEY,
    source_id TEXT NOT NULL,
    relative_folder TEXT NOT NULL,
    classification INTEGER NOT NULL,
    subject_name TEXT NULL,
    collection_name TEXT NULL,
    include_subfolders INTEGER NOT NULL DEFAULT 1,
    default_cutout_status INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(source_id) REFERENCES asset_sources(id) ON DELETE CASCADE,
    UNIQUE(source_id, relative_folder)
);
CREATE INDEX IF NOT EXISTS idx_folder_rules_source ON folder_rules(source_id);

ALTER TABLE assets ADD COLUMN source_id TEXT NULL;
ALTER TABLE assets ADD COLUMN source_relative_path TEXT NULL;
ALTER TABLE assets ADD COLUMN file_size INTEGER NOT NULL DEFAULT 0;
ALTER TABLE assets ADD COLUMN last_write_utc TEXT NULL;
ALTER TABLE assets ADD COLUMN extension TEXT NOT NULL DEFAULT '';
ALTER TABLE assets ADD COLUMN cutout_status INTEGER NOT NULL DEFAULT 0;
ALTER TABLE assets ADD COLUMN subject_name TEXT NULL;
ALTER TABLE assets ADD COLUMN collection_name TEXT NULL;
ALTER TABLE assets ADD COLUMN is_missing INTEGER NOT NULL DEFAULT 0;

UPDATE assets
SET source_relative_path = relative_path
WHERE source_relative_path IS NULL;

CREATE INDEX IF NOT EXISTS idx_assets_source ON assets(source_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_assets_source_rel
ON assets(source_id, source_relative_path)
WHERE source_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_assets_subject ON assets(subject_name);
CREATE INDEX IF NOT EXISTS idx_assets_collection ON assets(collection_name);
CREATE INDEX IF NOT EXISTS idx_assets_missing ON assets(is_missing);

UPDATE schema_info SET version = 2;
""";

    public const string V3 = """
ALTER TABLE voice_profiles ADD COLUMN speed_value INTEGER NULL;
ALTER TABLE voice_profiles ADD COLUMN pitch_value INTEGER NULL;
ALTER TABLE voice_profiles ADD COLUMN volume_value INTEGER NOT NULL DEFAULT 100;
ALTER TABLE voice_profiles ADD COLUMN sample_rate INTEGER NOT NULL DEFAULT 32000;

ALTER TABLE dialogue_lines ADD COLUMN voice_pitch_override INTEGER NULL;
ALTER TABLE dialogue_lines ADD COLUMN voice_speed_override INTEGER NULL;
ALTER TABLE dialogue_lines ADD COLUMN voice_volume_override INTEGER NULL;

-- The v1 rate field used ratio-like semantics, so it cannot be safely mapped to
-- the provider-specific native speed scales. Keep speed_value NULL (engine default).
UPDATE voice_profiles
SET pitch_value = CASE
    WHEN lower(provider_key) IN ('loquendo7-native', 'loquendo') THEN 50
    ELSE CAST(pitch AS INTEGER)
END
WHERE pitch_value IS NULL;

UPDATE voice_profiles
SET volume_value = CASE
    WHEN lower(provider_key) IN ('loquendo7-native', 'loquendo') THEN 50
    ELSE 100
END;

UPDATE schema_info SET version = 3;
""";

    public const string V4 = """
CREATE TABLE IF NOT EXISTS scene_script_blocks (
    id TEXT PRIMARY KEY,
    scene_id TEXT NOT NULL,
    order_index INTEGER NOT NULL,
    block_type INTEGER NOT NULL,
    character_id TEXT NULL,
    text_value TEXT NOT NULL DEFAULT '',
    asset_id TEXT NULL,
    voice_profile_id TEXT NULL,
    voice_pitch_override INTEGER NULL,
    voice_speed_override INTEGER NULL,
    voice_volume_override INTEGER NULL,
    pause_after_ms INTEGER NOT NULL DEFAULT 0,
    parameters_json TEXT NOT NULL DEFAULT '{}',
    start_offset_ms INTEGER NULL,
    generated_audio_path TEXT NULL,
    generated_audio_hash TEXT NULL,
    generated_duration_ms INTEGER NULL,
    FOREIGN KEY(scene_id) REFERENCES scenes(id) ON DELETE CASCADE,
    FOREIGN KEY(character_id) REFERENCES characters(id) ON DELETE SET NULL,
    FOREIGN KEY(asset_id) REFERENCES assets(id) ON DELETE SET NULL,
    FOREIGN KEY(voice_profile_id) REFERENCES voice_profiles(id) ON DELETE SET NULL,
    UNIQUE(scene_id, order_index)
);
CREATE INDEX IF NOT EXISTS idx_scene_script_blocks_scene ON scene_script_blocks(scene_id, order_index);
CREATE INDEX IF NOT EXISTS idx_scene_script_blocks_kind ON scene_script_blocks(block_type);

-- Preserve any dialogue rows created by experimental/older builds. Only copy when the
-- destination scene does not already have structured blocks.
INSERT INTO scene_script_blocks(
    id, scene_id, order_index, block_type, character_id, text_value, voice_profile_id,
    voice_pitch_override, voice_speed_override, voice_volume_override, pause_after_ms, parameters_json,
    start_offset_ms, generated_duration_ms)
SELECT
    d.id, d.scene_id, d.order_index, 0, d.character_id, d.text, d.voice_profile_id,
    d.voice_pitch_override, d.voice_speed_override, d.voice_volume_override, 0, '{}', d.start_offset_ms, d.duration_ms
FROM dialogue_lines d
WHERE NOT EXISTS (SELECT 1 FROM scene_script_blocks b WHERE b.scene_id = d.scene_id);

UPDATE schema_info SET version = 4;
""";

    public const string V5 = """
CREATE INDEX IF NOT EXISTS idx_assets_name_nocase ON assets(display_name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_assets_kind_source_path ON assets(kind, source_id, source_relative_path);
CREATE INDEX IF NOT EXISTS idx_assets_subject_nocase ON assets(subject_name COLLATE NOCASE);
UPDATE schema_info SET version = 5;
""";

    /// <summary>
    /// Full-text search (1.0.0-beta.3): one FTS5 document per asset with its name, path and the
    /// words of its Director tags (description, mood, role, subject), accent-insensitive. asset_fts_map
    /// gives each asset a stable document number (rowids of the assets table may change on VACUUM).
    /// Triggers keep it current; rescans that change nothing do not rewrite it.
    /// </summary>
    public const string SearchTables = """
CREATE TABLE IF NOT EXISTS asset_fts_map (
    asset_id TEXT PRIMARY KEY,
    doc INTEGER NOT NULL UNIQUE
);
CREATE VIRTUAL TABLE IF NOT EXISTS asset_search USING fts5(
    name, path, tags,
    tokenize = 'unicode61 remove_diacritics 2',
    prefix = '2 3'
);
DELETE FROM asset_search;
DELETE FROM asset_fts_map;
INSERT INTO asset_fts_map(asset_id, doc) SELECT id, row_number() OVER (ORDER BY id) FROM assets;
INSERT INTO asset_search(rowid, name, path, tags)
    SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((
        SELECT group_concat(t.value, ' ') FROM asset_tags t
        WHERE t.asset_id = a.id AND t.key LIKE 'director.%'
          AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
    FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id;
""";

    /// <summary>
    /// Triggers that keep the search index current. They must not rely on "INSERT OR IGNORE": when the
    /// statement that fires them is an UPSERT (INSERT … ON CONFLICT DO UPDATE, as UpsertAssetAsync),
    /// SQLite runs the trigger statements with ABORT and ignores their own conflict clause. The beta.3
    /// triggers did, so a rescan that renamed, moved or re-labelled a file failed with
    /// "UNIQUE constraint failed: asset_fts_map.asset_id". V7 replaces them with these.
    /// </summary>
    public const string SearchTriggers = """
DROP TRIGGER IF EXISTS trg_assets_search_insert;
DROP TRIGGER IF EXISTS trg_assets_search_update;
DROP TRIGGER IF EXISTS trg_assets_search_delete;
DROP TRIGGER IF EXISTS trg_asset_tags_search_insert;
DROP TRIGGER IF EXISTS trg_asset_tags_search_update;
DROP TRIGGER IF EXISTS trg_asset_tags_search_delete;
CREATE TRIGGER trg_assets_search_insert AFTER INSERT ON assets BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT INTO asset_fts_map(asset_id, doc)
        SELECT NEW.id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1
        WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.id)
          AND NOT EXISTS (SELECT 1 FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.id;
END;
CREATE TRIGGER trg_assets_search_update AFTER UPDATE ON assets
WHEN OLD.display_name IS NOT NEW.display_name OR OLD.source_relative_path IS NOT NEW.source_relative_path
  OR OLD.relative_path IS NOT NEW.relative_path OR OLD.subject_name IS NOT NEW.subject_name
  OR OLD.collection_name IS NOT NEW.collection_name BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT INTO asset_fts_map(asset_id, doc)
        SELECT NEW.id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1
        WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.id)
          AND NOT EXISTS (SELECT 1 FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.id;
END;
CREATE TRIGGER trg_assets_search_delete AFTER DELETE ON assets BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = OLD.id);
    DELETE FROM asset_fts_map WHERE asset_id = OLD.id;
END;
CREATE TRIGGER trg_asset_tags_search_insert AFTER INSERT ON asset_tags
WHEN NEW.key LIKE 'director.%' BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT INTO asset_fts_map(asset_id, doc)
        SELECT NEW.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1
        WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.asset_id)
          AND NOT EXISTS (SELECT 1 FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.asset_id;
END;
CREATE TRIGGER trg_asset_tags_search_update AFTER UPDATE ON asset_tags
WHEN (OLD.key LIKE 'director.%' OR NEW.key LIKE 'director.%') AND (OLD.value IS NOT NEW.value OR OLD.key IS NOT NEW.key) BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT INTO asset_fts_map(asset_id, doc)
        SELECT NEW.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1
        WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.asset_id)
          AND NOT EXISTS (SELECT 1 FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.asset_id;
END;
CREATE TRIGGER trg_asset_tags_search_delete AFTER DELETE ON asset_tags
WHEN OLD.key LIKE 'director.%' BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = OLD.asset_id);
    INSERT INTO asset_fts_map(asset_id, doc)
        SELECT OLD.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1
        WHERE EXISTS (SELECT 1 FROM assets WHERE id = OLD.asset_id)
          AND NOT EXISTS (SELECT 1 FROM asset_fts_map WHERE asset_id = OLD.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = OLD.asset_id;
END;
""";

    /// <summary>Full-text index, used when it is created after the migrations (FTS5 was missing before).</summary>
    public const string SearchIndex = SearchTables + "\n" + SearchTriggers;

    /// <summary>The triggers as shipped in V6 (1.0.0-beta.3), kept so old databases replay history exactly.</summary>
    private const string SearchTriggersBeta3 = """
CREATE TRIGGER IF NOT EXISTS trg_assets_search_insert AFTER INSERT ON assets BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT OR IGNORE INTO asset_fts_map(asset_id, doc)
        SELECT NEW.id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1 WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.id;
END;
CREATE TRIGGER IF NOT EXISTS trg_assets_search_update AFTER UPDATE ON assets
WHEN OLD.display_name IS NOT NEW.display_name OR OLD.source_relative_path IS NOT NEW.source_relative_path
  OR OLD.relative_path IS NOT NEW.relative_path OR OLD.subject_name IS NOT NEW.subject_name
  OR OLD.collection_name IS NOT NEW.collection_name BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.id);
    INSERT OR IGNORE INTO asset_fts_map(asset_id, doc)
        SELECT NEW.id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1 WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.id;
END;
CREATE TRIGGER IF NOT EXISTS trg_assets_search_delete AFTER DELETE ON assets BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = OLD.id);
    DELETE FROM asset_fts_map WHERE asset_id = OLD.id;
END;
CREATE TRIGGER IF NOT EXISTS trg_asset_tags_search_insert AFTER INSERT ON asset_tags
WHEN NEW.key LIKE 'director.%' BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT OR IGNORE INTO asset_fts_map(asset_id, doc)
        SELECT NEW.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1 WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.asset_id;
END;
CREATE TRIGGER IF NOT EXISTS trg_asset_tags_search_update AFTER UPDATE ON asset_tags
WHEN (OLD.key LIKE 'director.%' OR NEW.key LIKE 'director.%') AND (OLD.value IS NOT NEW.value OR OLD.key IS NOT NEW.key) BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = NEW.asset_id);
    INSERT OR IGNORE INTO asset_fts_map(asset_id, doc)
        SELECT NEW.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1 WHERE EXISTS (SELECT 1 FROM assets WHERE id = NEW.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = NEW.asset_id;
END;
CREATE TRIGGER IF NOT EXISTS trg_asset_tags_search_delete AFTER DELETE ON asset_tags
WHEN OLD.key LIKE 'director.%' BEGIN
    DELETE FROM asset_search WHERE rowid = (SELECT doc FROM asset_fts_map WHERE asset_id = OLD.asset_id);
    INSERT OR IGNORE INTO asset_fts_map(asset_id, doc)
        SELECT OLD.asset_id, coalesce((SELECT max(doc) FROM asset_fts_map), 0) + 1 WHERE EXISTS (SELECT 1 FROM assets WHERE id = OLD.asset_id);
    INSERT INTO asset_search(rowid, name, path, tags)
        SELECT m.doc, a.display_name, coalesce(a.source_relative_path, a.relative_path), trim(coalesce(a.subject_name, '') || ' ' || coalesce(a.collection_name, '') || ' ' || coalesce((         SELECT group_concat(t.value, ' ') FROM asset_tags t         WHERE t.asset_id = a.id AND t.key LIKE 'director.%'           AND t.key NOT IN ('director.auto.hash', 'director.auto.model', 'director.auto.version')), ''))
        FROM assets a JOIN asset_fts_map m ON m.asset_id = a.id WHERE a.id = OLD.asset_id;
END;
""";

    public const string V6 = SearchTables + "\n" + SearchTriggersBeta3 + "\nUPDATE schema_info SET version = 6;";

    /// <summary>
    /// Cleanup (1.0.0-beta.4): scene_characters, dialogue_lines, effects and jobs were created by the
    /// first schema but never read or written by the app (dialogue rows were already copied to
    /// scene_script_blocks by V4). project_info loses its schema_version column, a third copy of the
    /// version that nothing read: the version is schema_info's. The table is rebuilt instead of
    /// DROP COLUMN so it works on any SQLite build. Kept on purpose: the legacy voice_profiles.rate/pitch
    /// columns (still written for older builds) and scenes.background_asset_id; removing them means
    /// rebuilding tables that other tables reference, for no gain.
    /// </summary>
    public const string V7 = """
DROP TABLE IF EXISTS scene_characters;
DROP TABLE IF EXISTS dialogue_lines;
DROP TABLE IF EXISTS effects;
DROP TABLE IF EXISTS jobs;

CREATE TABLE project_info_v7 (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
INSERT INTO project_info_v7(id, name, created_utc, updated_utc)
    SELECT id, name, created_utc, updated_utc FROM project_info;
DROP TABLE project_info;
ALTER TABLE project_info_v7 RENAME TO project_info;

UPDATE schema_info SET version = 7;
""";
}
