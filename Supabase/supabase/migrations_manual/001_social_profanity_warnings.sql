-- Running per-account profanity-warning counter, read/written by the social edge function's
-- POST /chat handler. Resets to 0 once the count reaches PROFANITY_WARNING_LIMIT and a real
-- timeout is issued via social_sanctions.
ALTER TABLE public.user_profiles ADD COLUMN IF NOT EXISTS profanity_warning_count integer NOT NULL DEFAULT 0;
