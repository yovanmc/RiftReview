using Microsoft.Data.Sqlite;

namespace RiftReview.Core.Data;

public sealed class RiftReviewDb : IDisposable
{
    public const int LatestSchemaVersion = 3;
    private readonly SqliteConnection _conn;

    private RiftReviewDb(SqliteConnection conn) => _conn = conn;

    public static RiftReviewDb Open(string connectionString)
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        Exec(conn, "PRAGMA foreign_keys=ON;");
        var db = new RiftReviewDb(conn);
        db.RunVersionedMigrations();
        return db;
    }

    public int GetSchemaVersion()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    private void RunVersionedMigrations()
    {
        var v = GetSchemaVersion();
        if (v < 1)
        {
            Exec(_conn, Schema);
            Exec(_conn, "PRAGMA user_version=1;");
        }
        if (v < 2)
        {
            Exec(_conn, @"CREATE TABLE IF NOT EXISTS lp_snapshots (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  taken_utc INTEGER NOT NULL,
  queue_type TEXT NOT NULL,
  tier TEXT NOT NULL,
  division TEXT NOT NULL,
  league_points INTEGER NOT NULL,
  wins INTEGER NOT NULL,
  losses INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_lp_taken ON lp_snapshots(taken_utc);");
            Exec(_conn, "PRAGMA user_version=2;");
        }
        if (v < 3)
        {
            Exec(_conn, @"ALTER TABLE matches ADD COLUMN kill_participation REAL;
ALTER TABLE matches ADD COLUMN damage_share REAL;
ALTER TABLE matches ADD COLUMN deaths_pre15 INTEGER;");
            Exec(_conn, "PRAGMA user_version=3;");
        }
    }

    private const string Schema = @"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS matches (
  match_id TEXT PRIMARY KEY,
  queue_id INTEGER NOT NULL,
  game_start_utc INTEGER NOT NULL,
  duration_s INTEGER NOT NULL,
  patch TEXT NOT NULL,
  my_champion_id INTEGER NOT NULL,
  my_team_position TEXT NOT NULL,
  win INTEGER NOT NULL,
  kills INTEGER NOT NULL, deaths INTEGER NOT NULL, assists INTEGER NOT NULL,
  cs INTEGER NOT NULL,
  cs_at_10 INTEGER, gold_diff_at_15 INTEGER,
  opponent_participant_id INTEGER, opponent_champion_id INTEGER,
  synced_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS match_detail (
  match_id TEXT PRIMARY KEY REFERENCES matches(match_id) ON DELETE CASCADE,
  match_json TEXT NOT NULL,
  timeline_json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_matches_start ON matches(game_start_utc DESC);
CREATE INDEX IF NOT EXISTS ix_matches_queue ON matches(queue_id);";

    public bool HasMatch(string id) => ScalarLong("SELECT COUNT(1) FROM matches WHERE match_id=$id", ("$id", (object)id)) > 0;

    public void UpsertMatch(MatchRow m, string matchJson, string timelineJson)
    {
        using var tx = _conn.BeginTransaction();
        using (var c = _conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO matches
              (match_id,queue_id,game_start_utc,duration_s,patch,my_champion_id,my_team_position,win,
               kills,deaths,assists,cs,cs_at_10,gold_diff_at_15,opponent_participant_id,opponent_champion_id,synced_at,
               kill_participation,damage_share,deaths_pre15)
              VALUES ($id,$q,$gs,$d,$p,$champ,$pos,$win,$k,$de,$a,$cs,$cs10,$g15,$opid,$ochamp,$sync,$kp,$ds,$pre15)
              ON CONFLICT(match_id) DO UPDATE SET
               queue_id=$q,game_start_utc=$gs,duration_s=$d,patch=$p,my_champion_id=$champ,my_team_position=$pos,
               win=$win,kills=$k,deaths=$de,assists=$a,cs=$cs,cs_at_10=$cs10,gold_diff_at_15=$g15,
               opponent_participant_id=$opid,opponent_champion_id=$ochamp,synced_at=$sync,
               kill_participation=$kp,damage_share=$ds,deaths_pre15=$pre15;";
            Bind(c, "$id", m.MatchId); Bind(c, "$q", m.QueueId); Bind(c, "$gs", m.GameStartUtc);
            Bind(c, "$d", m.DurationS); Bind(c, "$p", m.Patch); Bind(c, "$champ", m.MyChampionId);
            Bind(c, "$pos", m.MyTeamPosition); Bind(c, "$win", m.Win ? 1 : 0);
            Bind(c, "$k", m.Kills); Bind(c, "$de", m.Deaths); Bind(c, "$a", m.Assists); Bind(c, "$cs", m.Cs);
            Bind(c, "$cs10", (object?)m.CsAt10 ?? DBNull.Value);
            Bind(c, "$g15", (object?)m.GoldDiffAt15 ?? DBNull.Value);
            Bind(c, "$opid", (object?)m.OpponentParticipantId ?? DBNull.Value);
            Bind(c, "$ochamp", (object?)m.OpponentChampionId ?? DBNull.Value);
            Bind(c, "$sync", m.SyncedAt);
            Bind(c, "$kp", (object?)m.KillParticipation ?? DBNull.Value);
            Bind(c, "$ds", (object?)m.DamageShare ?? DBNull.Value);
            Bind(c, "$pre15", (object?)m.DeathsPre15 ?? DBNull.Value);
            c.ExecuteNonQuery();
        }
        using (var c = _conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO match_detail(match_id,match_json,timeline_json) VALUES($id,$m,$t)
              ON CONFLICT(match_id) DO UPDATE SET match_json=$m, timeline_json=$t;";
            Bind(c, "$id", m.MatchId); Bind(c, "$m", matchJson); Bind(c, "$t", timelineJson);
            c.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public MatchRow? GetMatch(string id)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT * FROM matches WHERE match_id=$id";
        Bind(c, "$id", id);
        using var r = c.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    public string? GetTimelineJson(string id) =>
        ScalarString("SELECT timeline_json FROM match_detail WHERE match_id=$id", ("$id", (object)id));

    public string? GetMatchJson(string id) =>
        ScalarString("SELECT match_json FROM match_detail WHERE match_id=$id", ("$id", (object)id));

    public IReadOnlyList<MatchRow> RecentMatches(bool rankedOnly, int limit)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT * FROM matches" +
            (rankedOnly ? " WHERE queue_id IN (420,440)" : "") +
            " ORDER BY game_start_utc DESC LIMIT $lim";
        Bind(c, "$lim", limit);
        var list = new List<MatchRow>();
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
        return list;
    }

    public IReadOnlyList<MatchRow> AllMatches(bool rankedOnly)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT * FROM matches" +
            (rankedOnly ? " WHERE queue_id IN (420,440)" : "") +
            " ORDER BY game_start_utc DESC";
        var list = new List<MatchRow>();
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
        return list;
    }

    public string? GetMeta(string key) =>
        ScalarString("SELECT value FROM meta WHERE key=$k", ("$k", (object)key));

    public void SetMeta(string key, string value)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v;";
        Bind(c, "$k", key); Bind(c, "$v", value); c.ExecuteNonQuery();
    }

    public void InsertLpSnapshot(LpSnapshot s)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = @"INSERT INTO lp_snapshots
        (taken_utc,queue_type,tier,division,league_points,wins,losses)
        VALUES ($t,$q,$tier,$div,$lp,$w,$l);";
        Bind(c, "$t", s.TakenUtc); Bind(c, "$q", s.QueueType); Bind(c, "$tier", s.Tier);
        Bind(c, "$div", s.Division); Bind(c, "$lp", s.LeaguePoints);
        Bind(c, "$w", s.Wins); Bind(c, "$l", s.Losses);
        c.ExecuteNonQuery();
    }

    public IReadOnlyList<LpSnapshot> GetLpSnapshots()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT taken_utc,queue_type,tier,division,league_points,wins,losses FROM lp_snapshots ORDER BY taken_utc";
        var list = new List<LpSnapshot>();
        using var r = c.ExecuteReader();
        while (r.Read())
            list.Add(new LpSnapshot(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
        return list;
    }

    private static MatchRow ReadRow(SqliteDataReader r) => new(
        r.GetString(r.GetOrdinal("match_id")),
        r.GetInt32(r.GetOrdinal("queue_id")),
        r.GetInt64(r.GetOrdinal("game_start_utc")),
        r.GetInt32(r.GetOrdinal("duration_s")),
        r.GetString(r.GetOrdinal("patch")),
        r.GetInt32(r.GetOrdinal("my_champion_id")),
        r.GetString(r.GetOrdinal("my_team_position")),
        r.GetInt32(r.GetOrdinal("win")) != 0,
        r.GetInt32(r.GetOrdinal("kills")),
        r.GetInt32(r.GetOrdinal("deaths")),
        r.GetInt32(r.GetOrdinal("assists")),
        r.GetInt32(r.GetOrdinal("cs")),
        GetNullableInt(r, "cs_at_10"),
        GetNullableInt(r, "gold_diff_at_15"),
        GetNullableInt(r, "opponent_participant_id"),
        GetNullableInt(r, "opponent_champion_id"),
        r.GetInt64(r.GetOrdinal("synced_at")),
        GetNullableDouble(r, "kill_participation"),
        GetNullableDouble(r, "damage_share"),
        GetNullableInt(r, "deaths_pre15"));

    private static int? GetNullableInt(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? null : r.GetInt32(o);
    }

    private static double? GetNullableDouble(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? null : r.GetDouble(o);
    }

    private long ScalarLong(string sql, params (string, object)[] ps)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = sql;
        foreach (var (n, v) in ps) Bind(c, n, v);
        return (long)c.ExecuteScalar()!; // COUNT(1) never returns null
    }

    private string? ScalarString(string sql, params (string, object)[] ps)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = sql;
        foreach (var (n, v) in ps) Bind(c, n, v);
        var o = c.ExecuteScalar();
        return o == null || o is DBNull ? null : (string)o;
    }

    private static void Bind(SqliteCommand c, string name, object value)
    {
        var p = c.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        c.Parameters.Add(p);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
