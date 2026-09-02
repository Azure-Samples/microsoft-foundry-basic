using chatui.Data.Entities;

namespace chatui.Services;

public sealed class TransientSessionStore
{
    private readonly object _gate = new();
    private readonly List<Conversation> _conversations = [];
    private readonly List<Message> _messages = [];
    private readonly List<Meeting> _meetings = [];
    private readonly List<Participant> _participants = [];
    private readonly List<Turn> _turns = [];
    private readonly List<TurnTranslation> _translations = [];
    private readonly List<MeetingSummary> _summaries = [];

    public IReadOnlyList<Conversation> ListConversations(Guid userId)
    {
        lock (_gate)
        {
            return _conversations
                .Where(c => c.UserAId == userId || c.UserBId == userId)
                .OrderByDescending(c => LastActivity(c.Id))
                .ToList();
        }
    }

    public Conversation? GetConversation(Guid conversationId, Guid userId)
    {
        lock (_gate)
        {
            return _conversations.FirstOrDefault(c => c.Id == conversationId
                && (c.UserAId == userId || c.UserBId == userId));
        }
    }

    public Conversation GetOrCreateConversation(User me, User peer)
    {
        lock (_gate)
        {
            var (userA, userB) = me.Id.CompareTo(peer.Id) < 0 ? (me, peer) : (peer, me);
            var existing = _conversations.FirstOrDefault(c => c.UserAId == userA.Id && c.UserBId == userB.Id);
            if (existing is not null) return existing;

            var created = new Conversation
            {
                Id = Guid.NewGuid(),
                UserAId = userA.Id,
                UserBId = userB.Id,
                UserA = userA,
                UserB = userB,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _conversations.Add(created);
            return created;
        }
    }

    public IReadOnlyList<Message> ListMessages(Guid conversationId, DateTimeOffset? before, int limit)
    {
        lock (_gate)
        {
            return _messages
                .Where(m => m.ConversationId == conversationId && (before is null || m.CreatedAt < before))
                .OrderByDescending(m => m.CreatedAt)
                .Take(limit)
                .OrderBy(m => m.CreatedAt)
                .ToList();
        }
    }

    public Message AddMessage(Message message)
    {
        lock (_gate)
        {
            message.Id = Guid.NewGuid();
            message.CreatedAt = DateTimeOffset.UtcNow;
            _messages.Add(message);
            return message;
        }
    }

    public DateTimeOffset LastActivity(Guid conversationId)
    {
        lock (_gate)
        {
            return _messages
                .Where(m => m.ConversationId == conversationId)
                .Select(m => m.CreatedAt)
                .DefaultIfEmpty(_conversations.First(c => c.Id == conversationId).CreatedAt)
                .Max();
        }
    }

    public IReadOnlyList<Meeting> ListMeetings(Guid userId)
    {
        lock (_gate)
        {
            return _meetings
                .Where(m => m.InitiatorId == userId || _participants.Any(p => p.MeetingId == m.Id && p.UserId == userId))
                .OrderByDescending(m => m.CreatedAt)
                .ToList();
        }
    }

    public Meeting CreateMeeting(User initiator, string title)
    {
        lock (_gate)
        {
            var meeting = new Meeting
            {
                Id = Guid.NewGuid(),
                InitiatorId = initiator.Id,
                Initiator = initiator,
                Title = title,
                Status = "active",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            var participant = new Participant
            {
                Id = Guid.NewGuid(),
                MeetingId = meeting.Id,
                Meeting = meeting,
                UserId = initiator.Id,
                User = initiator,
                Language = initiator.PreferredLanguage,
                Role = "initiator",
                JoinedAt = DateTimeOffset.UtcNow,
            };
            meeting.Participants.Add(participant);
            _meetings.Add(meeting);
            _participants.Add(participant);
            return meeting;
        }
    }

    public Meeting? GetMeeting(Guid meetingId, Guid userId)
    {
        lock (_gate)
        {
            return _meetings.FirstOrDefault(m => m.Id == meetingId
                && (m.InitiatorId == userId || _participants.Any(p => p.MeetingId == meetingId && p.UserId == userId)));
        }
    }

    public Meeting? FindMeeting(Guid meetingId)
    {
        lock (_gate)
        {
            return _meetings.FirstOrDefault(m => m.Id == meetingId);
        }
    }

    public Participant JoinMeeting(Meeting meeting, User user, string language)
    {
        lock (_gate)
        {
            var participant = _participants.FirstOrDefault(p => p.MeetingId == meeting.Id && p.UserId == user.Id);
            if (participant is null)
            {
                participant = new Participant
                {
                    Id = Guid.NewGuid(),
                    MeetingId = meeting.Id,
                    Meeting = meeting,
                    UserId = user.Id,
                    User = user,
                    Role = "listener",
                    JoinedAt = DateTimeOffset.UtcNow,
                };
                _participants.Add(participant);
                meeting.Participants.Add(participant);
            }
            participant.Language = language;
            participant.LeftAt = null;
            return participant;
        }
    }

    public void LeaveMeeting(Guid meetingId, Guid userId)
    {
        lock (_gate)
        {
            var participant = _participants.FirstOrDefault(p => p.MeetingId == meetingId && p.UserId == userId);
            if (participant is not null) participant.LeftAt = DateTimeOffset.UtcNow;
        }
    }

    public IReadOnlyList<Turn> ListTurns(Guid meetingId, DateTimeOffset? before, int limit)
    {
        lock (_gate)
        {
            return _turns
                .Where(t => t.MeetingId == meetingId && (before is null || t.CreatedAt < before))
                .OrderByDescending(t => t.CreatedAt)
                .Take(limit)
                .OrderBy(t => t.CreatedAt)
                .ToList();
        }
    }

    public Turn AddTurn(Turn turn, IReadOnlyDictionary<string, string> translations)
    {
        lock (_gate)
        {
            turn.Id = Guid.NewGuid();
            turn.CreatedAt = DateTimeOffset.UtcNow;
            foreach (var (language, text) in translations)
            {
                var translation = new TurnTranslation { TurnId = turn.Id, Turn = turn, TargetLang = language, Translated = text };
                turn.Translations.Add(translation);
                _translations.Add(translation);
            }
            _turns.Add(turn);
            return turn;
        }
    }

    public MeetingSummary UpsertSummary(Guid meetingId, string language, string summary)
    {
        lock (_gate)
        {
            var existing = _summaries.FirstOrDefault(s => s.MeetingId == meetingId && s.Language == language);
            if (existing is null)
            {
                existing = new MeetingSummary { MeetingId = meetingId, Language = language };
                _summaries.Add(existing);
            }
            existing.Summary = summary;
            existing.GeneratedAt = DateTimeOffset.UtcNow;
            return existing;
        }
    }

    public MeetingSummary? GetSummary(Guid meetingId, string language)
    {
        lock (_gate)
        {
            return _summaries.FirstOrDefault(s => s.MeetingId == meetingId && s.Language == language);
        }
    }
}