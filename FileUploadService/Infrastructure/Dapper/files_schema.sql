-- ============================================================
--  TABLE: microservices
--  Represents the calling service/client that uploaded a file.
--  uploaded_by in the files table references this.
-- ============================================================
CREATE TABLE microservices (
    id          BIGSERIAL    PRIMARY KEY,          -- simple 1, 2, 3 ...
    name        VARCHAR(100) UNIQUE NOT NULL,      -- e.g. "billing-service"
    description TEXT,
    api_key     VARCHAR(255),                      -- optional auth identifier
    created_at  TIMESTAMP    NOT NULL DEFAULT NOW()
);


-- ============================================================
--  TABLE: files
--  Changes vs old schema:
--    • id       : BIGSERIAL instead of UUID  (gives you 1, 2, 3 ...)
--    • uploaded_by : BIGINT FK → microservices(id)  instead of bare UUID
--    • is_encrypted : REMOVED (every file is always encrypted; IV presence
--                    is the implicit proof — no flag needed)
-- ============================================================
CREATE TABLE files (
    id                BIGSERIAL     PRIMARY KEY,          -- 1, 2, 3 ...
    reference_id      VARCHAR(50)   UNIQUE NOT NULL,      -- FILE-20260526-ABC123
    original_filename VARCHAR(255)  NOT NULL,
    storage_path      TEXT          NOT NULL,
    content_type      VARCHAR(100),
    file_size         BIGINT        NOT NULL,
    uploaded_by       BIGINT        REFERENCES microservices(id) ON DELETE SET NULL,
    created_at        TIMESTAMP     NOT NULL DEFAULT NOW(),
    iv                TEXT          NOT NULL              -- AES IV, always present
);


-- ============================================================
--  MIGRATION — run this if you already have the old schema
--  (skip if starting fresh with the CREATE TABLE above)
-- ============================================================

-- Step 1: add microservices table
-- (already done above if starting fresh)

-- Step 2: swap id from UUID to BIGSERIAL
--   PostgreSQL cannot alter a UUID column type in-place to BIGSERIAL.
--   Safe migration path:
ALTER TABLE files ADD COLUMN new_id BIGSERIAL;
ALTER TABLE files DROP CONSTRAINT files_pkey;
ALTER TABLE files ADD PRIMARY KEY (new_id);
ALTER TABLE files DROP COLUMN id;
ALTER TABLE files RENAME COLUMN new_id TO id;

-- Step 3: swap uploaded_by from UUID to BIGINT FK
ALTER TABLE files DROP COLUMN uploaded_by;
ALTER TABLE files ADD COLUMN uploaded_by BIGINT REFERENCES microservices(id) ON DELETE SET NULL;

-- Step 4: remove is_encrypted
ALTER TABLE files DROP COLUMN IF EXISTS is_encrypted;

-- Step 5: make iv NOT NULL now that the flag is gone
--   (set any legacy NULLs to empty string first if needed, then constrain)
UPDATE files SET iv = '' WHERE iv IS NULL;
ALTER TABLE files ALTER COLUMN iv SET NOT NULL;


-- ============================================================
--  USEFUL QUERIES
-- ============================================================

-- All files with the microservice name that uploaded them
SELECT
    f.id,
    f.reference_id,
    f.original_filename,
    f.content_type,
    f.file_size,
    f.created_at,
    m.name AS uploaded_by_service
FROM files f
LEFT JOIN microservices m ON m.id = f.uploaded_by
ORDER BY f.created_at DESC;

-- Latest file per microservice
SELECT DISTINCT ON (f.uploaded_by)
    m.name,
    f.reference_id,
    f.original_filename,
    f.created_at
FROM files f
JOIN microservices m ON m.id = f.uploaded_by
ORDER BY f.uploaded_by, f.created_at DESC;

-- Column list check
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_name = 'files'
ORDER BY ordinal_position;