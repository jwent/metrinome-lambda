-- Seeds campaign, click, conversion, and duplicate postback data for one user/year.
--
-- Intended to be run through seed-jeremy-year-data.ps1, which supplies:
--   seed_email, campaign_name, seed_year, start_date, end_date,
--   min_clicks_per_day, max_clicks_per_day,
--   bot_every_n_days, conversion_every_n_days, duplicate_every_n_conversions.
--
-- The script is idempotent for the campaign/year seed key.
--
-- If the campaign already exists, it is reused. The script removes only prior
-- seeded clicks/CVEs identified by the generated seed key; it does not remove
-- any non-seeded campaign data.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TEMP TABLE _seed_params AS
SELECT
    :'seed_email'::text AS email,
    :'campaign_name'::text AS campaign_name,
    :'seed_year'::int AS seed_year,
    :'start_date'::date AS start_date,
    :'end_date'::date AS end_date,
    GREATEST(:'min_clicks_per_day'::int, 1) AS min_clicks_per_day,
    GREATEST(:'max_clicks_per_day'::int, :'min_clicks_per_day'::int, 1) AS max_clicks_per_day,
    GREATEST(:'bot_every_n_days'::int, 1) AS bot_every_n_days,
    GREATEST(:'conversion_every_n_days'::int, 1) AS conversion_every_n_days,
    GREATEST(:'duplicate_every_n_conversions'::int, 1) AS duplicate_every_n_conversions,
    ('seed-' || :'seed_year'::text || '-brand-search-demo-cve-data') AS seed_key;

DO $$
DECLARE
    target_user_id uuid;
    target_org_id uuid;
    existing_count int;
BEGIN
    SELECT u."Id", u."OrganizationId"
    INTO target_user_id, target_org_id
    FROM "Users" u
    JOIN _seed_params p ON lower(u."Email") = lower(p.email)
    LIMIT 1;

    IF target_user_id IS NULL OR target_org_id IS NULL THEN
        RAISE EXCEPTION 'Target user % was not found or has no organization.', (SELECT email FROM _seed_params);
    END IF;

    SELECT count(*)
    INTO existing_count
    FROM "TrackingCampaigns" c
    JOIN "UserTrackers" t ON t."Id" = c."ParentTrackerId"
    JOIN _seed_params p ON p.campaign_name = c."CampaignName"
    WHERE t."OrganizationId" = target_org_id;

    RAISE NOTICE 'Replacing % existing seeded campaign(s) for % in org %.', existing_count, (SELECT email FROM _seed_params), target_org_id;
END $$;

CREATE TEMP TABLE _target AS
SELECT
    u."Id" AS user_id,
    u."OrganizationId" AS organization_id,
    COALESCE(
        (
            SELECT t."Id"
            FROM "UserTrackers" t
            WHERE t."OrganizationId" = u."OrganizationId"
            ORDER BY t."CreatedAt"
            LIMIT 1
        ),
        gen_random_uuid()
    ) AS tracker_id
FROM "Users" u
JOIN _seed_params p ON lower(u."Email") = lower(p.email)
LIMIT 1;

INSERT INTO "UserTrackers" ("Id", "OrganizationId", "CreatedAt")
SELECT t.tracker_id, t.organization_id, now()
FROM _target t
WHERE NOT EXISTS (
    SELECT 1
    FROM "UserTrackers" existing
    WHERE existing."Id" = t.tracker_id
);

CREATE TEMP TABLE _seed_campaign AS
WITH existing AS (
    SELECT c."Id", c."ParentTrackerId"
    FROM "TrackingCampaigns" c
    JOIN "UserTrackers" t ON t."Id" = c."ParentTrackerId"
    JOIN _target target ON target.organization_id = t."OrganizationId"
    JOIN _seed_params p ON p.campaign_name = c."CampaignName"
    ORDER BY c."CreatedAt"
    LIMIT 1
),
inserted AS (
    INSERT INTO "TrackingCampaigns" (
        "Id",
        "ParentTrackerId",
        "CreatedAt",
        "Audience",
        "Platform",
        "CampaignName",
        "CampaignBudget",
        "ConversionValue",
        "WebsiteDomain",
        "CartPageURL",
        "LandingPageURL",
        "PrivacyPageURL"
    )
    SELECT
        gen_random_uuid(),
        target.tracker_id,
        p.start_date::timestamp,
        50000,
        'google',
        p.campaign_name,
        '40000',
        '250',
        'seeded.example.com',
        'https://seeded.example.com/cart',
        'https://seeded.example.com/landing',
        'https://seeded.example.com/privacy'
    FROM _target target
    CROSS JOIN _seed_params p
    WHERE NOT EXISTS (SELECT 1 FROM existing)
    RETURNING "Id", "ParentTrackerId"
)
SELECT
    "Id" AS campaign_id,
    "ParentTrackerId" AS tracker_id
FROM existing
UNION ALL
SELECT
    "Id" AS campaign_id,
    "ParentTrackerId" AS tracker_id
FROM inserted;

-- Remove prior seed-generated rows for this campaign/year only. Non-seeded
-- campaign data is preserved.
WITH seed_clicks AS (
    SELECT tc."Id"
    FROM "TrackerClicks" tc
    JOIN _seed_campaign campaign ON campaign.campaign_id = tc."CampaignId"
    JOIN _seed_params p ON tc."ClickUrl" LIKE ('%seed=' || p.seed_key || '%')
)
DELETE FROM "ConversionVerificationEvents" cve
USING seed_clicks sc
WHERE cve."TrackerClickId" = sc."Id";

WITH seed_clicks AS (
    SELECT tc."Id"
    FROM "TrackerClicks" tc
    JOIN _seed_campaign campaign ON campaign.campaign_id = tc."CampaignId"
    JOIN _seed_params p ON tc."ClickUrl" LIKE ('%seed=' || p.seed_key || '%')
)
DELETE FROM "TrackerClickExtraProperties" ep
USING seed_clicks sc
WHERE ep."ClickParentId" = sc."Id";

DELETE FROM "TrackerClicks" tc
USING _seed_campaign campaign, _seed_params p
WHERE tc."CampaignId" = campaign.campaign_id
  AND tc."ClickUrl" LIKE ('%seed=' || p.seed_key || '%');

INSERT INTO "TrackingCampaignExtraProperties" ("Id", "ParentId", "PropertyKey", "PropertyValue")
SELECT gen_random_uuid(), campaign_id, 'CampaignType', 'Search Campaign - Regular Reg'
FROM _seed_campaign campaign
WHERE NOT EXISTS (
    SELECT 1
    FROM "TrackingCampaignExtraProperties" existing
    WHERE existing."ParentId" = campaign.campaign_id
      AND existing."PropertyKey" = 'CampaignType'
);

CREATE TEMP TABLE _seed_site AS
WITH upserted AS (
    INSERT INTO "OrganizationSites" (
        "Id",
        "OrganizationId",
        "SiteName",
        "Domain",
        "TrackingId",
        "IsActive",
        "CreatedAt",
        "UpdatedAt"
    )
    SELECT
        gen_random_uuid(),
        target.organization_id,
        'Seeded Demo Site',
        'seeded.example.com',
        p.seed_key,
        true,
        now(),
        now()
    FROM _target target
    CROSS JOIN _seed_params p
    ON CONFLICT ("OrganizationId", "Domain") DO UPDATE
        SET "SiteName" = EXCLUDED."SiteName",
            "TrackingId" = EXCLUDED."TrackingId",
            "IsActive" = true,
            "UpdatedAt" = now()
    RETURNING "Id"
)
SELECT "Id" AS site_id
FROM upserted;

CREATE TEMP TABLE _seed_days AS
SELECT
    row_number() OVER (ORDER BY day_value)::int AS day_index,
    day_value::date AS day_value
FROM _seed_params p
CROSS JOIN generate_series(p.start_date, p.end_date, interval '1 day') AS generated_days(day_value);

INSERT INTO "TrackerClicks" (
    "Id",
    "ParentTrackerId",
    "CampaignId",
    "CreatedAt",
    "Ip",
    "ClickUrl",
    "Useragent",
    "Referer",
    "IsBotClick",
    "ConversionDate",
    "Conversion",
    "IsDesktop"
)
SELECT
    gen_random_uuid(),
    campaign.tracker_id,
    campaign.campaign_id,
    (
        days.day_value::timestamp
        + make_interval(
            hours => 6 + floor(random() * 16)::int,
            mins => floor(random() * 60)::int,
            secs => floor(random() * 60)::int
        )
    ),
    CASE
        WHEN clicks.click_index = clicks.day_click_count AND random() < 0.45
            THEN '203.0.113.' || ((days.day_index % 200) + 1)::text
        ELSE '198.51.' || ((floor(random() * 80)::int) + 1)::text || '.' || ((floor(random() * 220)::int) + 1)::text
    END,
    'https://seeded.example.com/landing?seed=' || p.seed_key || '&d=' || days.day_index || '&c=' || clicks.click_index,
    CASE
        WHEN clicks.click_index = clicks.day_click_count AND random() < 0.45
            THEN 'Mozilla/5.0 compatible bot seeded-test'
        WHEN random() < 0.35
            THEN 'Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148'
        ELSE 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/125.0 Safari/537.36'
    END,
    CASE
        WHEN random() < 0.45 THEN 'https://google.com'
        WHEN random() < 0.65 THEN 'https://linkedin.com'
        WHEN random() < 0.82 THEN 'https://facebook.com'
        ELSE 'direct'
    END,
    clicks.click_index = clicks.day_click_count AND days.day_index % p.bot_every_n_days = 0,
    CASE
        WHEN days.day_index % p.conversion_every_n_days = 0 AND clicks.click_index <= GREATEST(1, floor(clicks.day_click_count * 0.2)::int)
            THEN days.day_value::timestamp + make_interval(hours => 7 + floor(random() * 14)::int, mins => floor(random() * 60)::int)
        ELSE NULL
    END,
    days.day_index % p.conversion_every_n_days = 0 AND clicks.click_index <= GREATEST(1, floor(clicks.day_click_count * 0.2)::int),
    random() < 0.68
FROM _seed_days days
CROSS JOIN _seed_params p
CROSS JOIN _seed_campaign campaign
CROSS JOIN LATERAL (
    SELECT GREATEST(
        p.min_clicks_per_day,
        p.min_clicks_per_day + floor(random() * (p.max_clicks_per_day - p.min_clicks_per_day + 1))::int
    ) AS day_click_count
) click_volume
CROSS JOIN LATERAL generate_series(1, click_volume.day_click_count) AS generated_clicks(click_index)
CROSS JOIN LATERAL (
    SELECT generated_clicks.click_index, click_volume.day_click_count
) AS clicks;

CREATE TEMP TABLE _seed_clicks AS
SELECT
    tc."Id" AS click_id,
    tc."CreatedAt" AS created_at,
    tc."Conversion" = true AS is_conversion,
    tc."IsBotClick" = true AS is_bot,
    row_number() OVER (ORDER BY tc."CreatedAt", tc."Id") AS row_number
FROM "TrackerClicks" tc
JOIN _seed_campaign campaign ON campaign.campaign_id = tc."CampaignId"
JOIN _seed_params p ON tc."ClickUrl" LIKE ('%seed=' || p.seed_key || '%');

INSERT INTO "TrackerClickExtraProperties" ("Id", "ClickParentId", "PropertyKey", "PropertyValue")
SELECT gen_random_uuid(), click_id, 'ip_country', 'US'
FROM _seed_clicks
UNION ALL
SELECT gen_random_uuid(), click_id, 'ip_region',
    CASE (row_number % 5)
        WHEN 0 THEN 'NY'
        WHEN 1 THEN 'CA'
        WHEN 2 THEN 'TX'
        WHEN 3 THEN 'FL'
        ELSE 'IL'
    END
FROM _seed_clicks
UNION ALL
SELECT gen_random_uuid(), click_id, 'ip_city',
    CASE (row_number % 5)
        WHEN 0 THEN 'New York'
        WHEN 1 THEN 'San Francisco'
        WHEN 2 THEN 'Austin'
        WHEN 3 THEN 'Miami'
        ELSE 'Chicago'
    END
FROM _seed_clicks;

CREATE TEMP TABLE _seed_verified_cves (
    event_id uuid NOT NULL,
    tracker_click_id uuid NOT NULL,
    submitted_at_utc timestamp with time zone NOT NULL,
    ordinal int NOT NULL
);

WITH inserted AS (
    INSERT INTO "ConversionVerificationEvents" (
        "Id",
        "OrganizationId",
        "SiteId",
        "ContractId",
        "TrackerId",
        "TrackingCampaignId",
        "TrackerClickId",
        "ExternalSubmissionId",
        "ExternalConversionId",
        "IdempotencyKey",
        "SubmittedAtUtc",
        "OriginalEventTimestampUtc",
        "Status",
        "CountsTowardCve",
        "CountedAtUtc",
        "DuplicateOfEventId",
        "RejectionReason",
        "RequestHash",
        "Source",
        "CreatedAtUtc",
        "UpdatedAtUtc"
    )
    SELECT
        gen_random_uuid(),
        target.organization_id,
        site.site_id,
        (
            SELECT contract."Id"
            FROM "OrganizationCveContracts" contract
            WHERE contract."OrganizationId" = target.organization_id
              AND seed_clicks.created_at::date BETWEEN contract."ContractStartDate"::date AND contract."ContractEndDate"::date
            ORDER BY contract."ContractStartDate" DESC
            LIMIT 1
        ),
        campaign.tracker_id,
        campaign.campaign_id,
        seed_clicks.click_id,
        seed_clicks.click_id::text,
        seed_clicks.click_id::text,
        p.seed_key || ':verified:' || seed_clicks.click_id::text,
        seed_clicks.created_at + interval '20 minutes',
        seed_clicks.created_at + interval '20 minutes',
        'Verified',
        true,
        seed_clicks.created_at + interval '20 minutes',
        NULL,
        NULL,
        md5(p.seed_key || ':verified:' || seed_clicks.click_id::text),
        'seed_script',
        seed_clicks.created_at + interval '20 minutes',
        seed_clicks.created_at + interval '20 minutes'
    FROM _seed_clicks seed_clicks
    CROSS JOIN _target target
    CROSS JOIN _seed_campaign campaign
    CROSS JOIN _seed_site site
    CROSS JOIN _seed_params p
    WHERE seed_clicks.is_conversion
    RETURNING "Id", "TrackerClickId", "SubmittedAtUtc"
)
INSERT INTO _seed_verified_cves (event_id, tracker_click_id, submitted_at_utc, ordinal)
SELECT
    inserted."Id",
    inserted."TrackerClickId",
    inserted."SubmittedAtUtc",
    row_number() OVER (ORDER BY inserted."SubmittedAtUtc", inserted."Id")::int
FROM inserted;

INSERT INTO "ConversionVerificationEvents" (
    "Id",
    "OrganizationId",
    "SiteId",
    "ContractId",
    "TrackerId",
    "TrackingCampaignId",
    "TrackerClickId",
    "ExternalSubmissionId",
    "ExternalConversionId",
    "IdempotencyKey",
    "SubmittedAtUtc",
    "OriginalEventTimestampUtc",
    "Status",
    "CountsTowardCve",
    "CountedAtUtc",
    "DuplicateOfEventId",
    "RejectionReason",
    "RequestHash",
    "Source",
    "CreatedAtUtc",
    "UpdatedAtUtc"
)
SELECT
    gen_random_uuid(),
    target.organization_id,
    site.site_id,
    (
        SELECT contract."Id"
        FROM "OrganizationCveContracts" contract
        WHERE contract."OrganizationId" = target.organization_id
          AND verified.submitted_at_utc::date BETWEEN contract."ContractStartDate"::date AND contract."ContractEndDate"::date
        ORDER BY contract."ContractStartDate" DESC
        LIMIT 1
    ),
    campaign.tracker_id,
    campaign.campaign_id,
    verified.tracker_click_id,
    verified.tracker_click_id::text,
    verified.tracker_click_id::text,
    p.seed_key || ':duplicate:' || verified.tracker_click_id::text,
    verified.submitted_at_utc + interval '2 minutes',
    verified.submitted_at_utc,
    'Duplicate',
    true,
    verified.submitted_at_utc + interval '2 minutes',
    verified.event_id,
    NULL,
    md5(p.seed_key || ':duplicate:' || verified.tracker_click_id::text),
    'seed_script',
    verified.submitted_at_utc + interval '2 minutes',
    verified.submitted_at_utc + interval '2 minutes'
FROM _seed_verified_cves verified
CROSS JOIN _target target
CROSS JOIN _seed_campaign campaign
CROSS JOIN _seed_site site
CROSS JOIN _seed_params p
WHERE verified.ordinal % p.duplicate_every_n_conversions = 0;

SELECT
    p.email AS target_email,
    p.campaign_name,
    p.start_date,
    p.end_date,
    (SELECT count(*) FROM _seed_clicks) AS seeded_clicks,
    (SELECT count(*) FROM _seed_clicks WHERE is_bot) AS seeded_bot_clicks,
    (SELECT count(*) FROM _seed_clicks WHERE is_conversion) AS seeded_converted_clicks,
    (SELECT count(*) FROM _seed_verified_cves) AS seeded_verified_postbacks,
    (
        SELECT count(*)
        FROM "ConversionVerificationEvents" cve
        JOIN _seed_campaign campaign ON campaign.campaign_id = cve."TrackingCampaignId"
        WHERE cve."Status" = 'Duplicate'
    ) AS seeded_duplicate_postbacks,
    (SELECT campaign_id FROM _seed_campaign) AS campaign_id
FROM _seed_params p;
