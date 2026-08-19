using System;
using System.Collections.Generic;
using UnityEngine;

namespace Molae.Core
{
    /// <summary>
    /// 최고 기록 상위 3개를 기기에 저장한다.
    ///
    /// PlayerPrefs 를 쓰는 이유: 기록 3개는 수십 바이트짜리 데이터다.
    /// 이 정도에 파일 IO 를 붙이면 저장 도중 앱이 죽었을 때 파일이 깨질 위험만 는다.
    /// PlayerPrefs 는 안드로이드에서 SharedPreferences 로 내려가고 원자적 커밋을 보장한다.
    ///
    /// 저장 시점을 Save() 로 명시하는 이유: PlayerPrefs 는 앱이 정상 종료될 때만
    /// 자동 flush 된다. 게임을 강제 종료하는 사용자가 대부분이므로 기록이 갱신될 때마다
    /// 즉시 디스크에 내려야 한다.
    /// </summary>
    public static class ScoreBoard
    {
        public const int Capacity = 3;

        private const string KeyScore = "Molae.Record.Score.";
        private const string KeyRound = "Molae.Record.Round.";
        private const string KeyDate = "Molae.Record.Date.";
        private const string KeyCleared = "Molae.Record.Cleared.";

        [Serializable]
        public struct Record
        {
            public int score;
            /// <summary>도달한 교시(1~3).</summary>
            public int round;
            /// <summary>3교시까지 전부 깼는지.</summary>
            public bool cleared;
            /// <summary>"MM/dd" 형식.</summary>
            public string date;

            public bool IsEmpty => score <= 0;
        }

        /// <summary>상위 3개를 점수 내림차순으로 반환한다. 빈 칸도 포함해 항상 3개.</summary>
        public static List<Record> Load()
        {
            var list = new List<Record>(Capacity);
            for (int i = 0; i < Capacity; i++)
            {
                list.Add(new Record
                {
                    score = PlayerPrefs.GetInt(KeyScore + i, 0),
                    round = PlayerPrefs.GetInt(KeyRound + i, 0),
                    cleared = PlayerPrefs.GetInt(KeyCleared + i, 0) == 1,
                    date = PlayerPrefs.GetString(KeyDate + i, ""),
                });
            }
            return list;
        }

        /// <summary>
        /// 기록을 제출한다.
        /// </summary>
        /// <returns>순위에 들었으면 1~3, 못 들었으면 0.</returns>
        public static int Submit(int score, int round, bool cleared)
        {
            if (score <= 0) return 0;

            var list = Load();
            list.Add(new Record
            {
                score = score,
                round = round,
                cleared = cleared,
                date = DateTime.Now.ToString("MM/dd"),
            });

            // 동점이면 더 높은 교시가 위로. 그것도 같으면 기존 기록을 유지한다
            // (새 기록이 옛 기록을 밀어내지 않도록 안정 정렬처럼 동작시킨다).
            list.Sort((a, b) =>
            {
                int c = b.score.CompareTo(a.score);
                if (c != 0) return c;
                return b.round.CompareTo(a.round);
            });

            int rank = 0;
            for (int i = 0; i < Capacity && i < list.Count; i++)
            {
                if (list[i].score == score && list[i].round == round && rank == 0)
                    rank = i + 1;
            }

            for (int i = 0; i < Capacity; i++)
            {
                Record r = i < list.Count ? list[i] : default;
                PlayerPrefs.SetInt(KeyScore + i, r.score);
                PlayerPrefs.SetInt(KeyRound + i, r.round);
                PlayerPrefs.SetInt(KeyCleared + i, r.cleared ? 1 : 0);
                PlayerPrefs.SetString(KeyDate + i, r.date ?? "");
            }
            PlayerPrefs.Save();   // 강제 종료에 대비해 즉시 디스크로

            return rank;
        }

        /// <summary>1위 점수. 없으면 0.</summary>
        public static int Best => PlayerPrefs.GetInt(KeyScore + 0, 0);

        public static void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                PlayerPrefs.DeleteKey(KeyScore + i);
                PlayerPrefs.DeleteKey(KeyRound + i);
                PlayerPrefs.DeleteKey(KeyCleared + i);
                PlayerPrefs.DeleteKey(KeyDate + i);
            }
            PlayerPrefs.Save();
        }

        /// <summary>타이틀 화면에 뿌릴 한 줄 문자열. 빈 칸은 점선으로 채운다.</summary>
        public static string FormatLine(int index, Record r)
        {
            string medal = index == 0 ? "1" : index == 1 ? "2" : "3";
            if (r.IsEmpty) return $"{medal}   - - - - -";
            string tag = r.cleared ? "탈출" : $"{r.round}교시";
            return $"{medal}   {r.score:N0}   <size=70%>{tag}  {r.date}</size>";
        }
    }
}
