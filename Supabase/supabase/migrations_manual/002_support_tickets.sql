-- Support ticket system + notification centre, Supabase-native equivalent of Pronwan's
-- Laravel "tickets"/"notifications" API groups. Identity is steam_id throughout, same
-- convention as every other table in this project.

CREATE TABLE IF NOT EXISTS public.support_tickets (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    steam_id text NOT NULL,
    category text NOT NULL DEFAULT 'other',
    status text NOT NULL DEFAULT 'open',
    resolution text,
    subject text NOT NULL,
    body text NOT NULL,
    meta jsonb NOT NULL DEFAULT '{}'::jsonb,
    sanction_id uuid REFERENCES public.social_sanctions(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_support_tickets_steam_id ON public.support_tickets(steam_id);

CREATE TABLE IF NOT EXISTS public.support_ticket_messages (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ticket_id uuid NOT NULL REFERENCES public.support_tickets(id) ON DELETE CASCADE,
    author_steam_id text NOT NULL,
    is_staff boolean NOT NULL DEFAULT false,
    kind text NOT NULL DEFAULT 'message',
    body text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_support_ticket_messages_ticket_id ON public.support_ticket_messages(ticket_id);

-- Hangs off either the ticket itself (initial filing) or one reply — never both, so a single
-- FK pair with a check keeps a stray attachment from floating unowned.
CREATE TABLE IF NOT EXISTS public.support_attachments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ticket_id uuid REFERENCES public.support_tickets(id) ON DELETE CASCADE,
    message_id uuid REFERENCES public.support_ticket_messages(id) ON DELETE CASCADE,
    storage_path text NOT NULL,
    name text NOT NULL,
    size bigint NOT NULL DEFAULT 0,
    mime text NOT NULL DEFAULT 'application/octet-stream',
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT support_attachments_owner_chk CHECK (
        (ticket_id IS NOT NULL AND message_id IS NULL) OR (ticket_id IS NULL AND message_id IS NOT NULL)
    )
);

-- Per-account read state. A ticket has no single "unread" flag because staff and the filer
-- both read the same thread independently.
CREATE TABLE IF NOT EXISTS public.support_ticket_reads (
    ticket_id uuid NOT NULL REFERENCES public.support_tickets(id) ON DELETE CASCADE,
    steam_id text NOT NULL,
    read_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (ticket_id, steam_id)
);

CREATE TABLE IF NOT EXISTS public.support_notifications (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    steam_id text NOT NULL,
    type text NOT NULL DEFAULT 'notification',
    level text NOT NULL DEFAULT 'info',
    title text NOT NULL,
    body text NOT NULL DEFAULT '',
    url text,
    read_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_support_notifications_steam_id ON public.support_notifications(steam_id);

-- Private bucket: attachments are read only through the edge function (which checks ticket
-- ownership before minting a signed URL), never served directly off a public bucket URL.
INSERT INTO storage.buckets (id, name, public)
VALUES ('support-attachments', 'support-attachments', false)
ON CONFLICT (id) DO NOTHING;
