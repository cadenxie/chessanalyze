/* 演示数据:由脚本按着法合法性生成,评估与分类为手工编造,不代表引擎真实结论 */
export const DEMO = {
  "game": {
    "white": "Anderssen, Adolf",
    "black": "Kieseritzky, Lionel",
    "result": "1-0",
    "opening": "Evans Gambit",
    "eco": "C52",
    "date": "1851.06.21",
    "site": "London ENG",
    "whiteElo": 2500,
    "blackElo": 2200,
    "engine": "Stockfish 17.1 (演示数据)",
    "pgn": "[Event \"Immortal Game\"]\n[Site \"London ENG\"]\n[Date \"1851.06.21\"]\n[White \"Anderssen, Adolf\"]\n[Black \"Kieseritzky, Lionel\"]\n[Result \"1-0\"]\n[WhiteElo \"2500\"]\n[BlackElo \"2200\"]\n[ECO \"C52\"]\n[Opening \"Evans Gambit\"]\n\n1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. b4 Bxb4 5. c3 Ba5 6. d4 exd4 7. O-O d3 8. Qb3 Qf6\n9. e5 Qg6 10. Re1 Nge7 11. Ba3 b5 12. Qxb5 Rb8 13. Qa4 Bb6 14. Nbd2 Bb7 15. Ne4 Qf5\n16. Bxd3 Qh5 17. Nf6+ gxf6 18. exf6 Rg8 19. Rad1 Qxf3 20. Rxe7+ Nxe7 21. Qxd7+ Kxd7\n22. Bf5+ Ke8 23. Bd7+ Kf8 24. Bxe7# 1-0"
  },
  "moves": [
    {
      "ply": 1,
      "san": "e4",
      "uci": "e2e4",
      "fen": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1",
      "fenBefore": "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
    },
    {
      "ply": 2,
      "san": "e5",
      "uci": "e7e5",
      "fen": "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2",
      "fenBefore": "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"
    },
    {
      "ply": 3,
      "san": "Nf3",
      "uci": "g1f3",
      "fen": "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2",
      "fenBefore": "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2"
    },
    {
      "ply": 4,
      "san": "Nc6",
      "uci": "b8c6",
      "fen": "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
      "fenBefore": "rnbqkbnr/pppp1ppp/8/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2"
    },
    {
      "ply": 5,
      "san": "Bc4",
      "uci": "f1c4",
      "fen": "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3",
      "fenBefore": "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3"
    },
    {
      "ply": 6,
      "san": "Bc5",
      "uci": "f8c5",
      "fen": "r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
      "fenBefore": "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 3 3"
    },
    {
      "ply": 7,
      "san": "b4",
      "uci": "b2b4",
      "fen": "r1bqk1nr/pppp1ppp/2n5/2b1p3/1PB1P3/5N2/P1PP1PPP/RNBQK2R b KQkq - 0 4",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"
    },
    {
      "ply": 8,
      "san": "Bxb4",
      "uci": "c5b4",
      "fen": "r1bqk1nr/pppp1ppp/2n5/4p3/1bB1P3/5N2/P1PP1PPP/RNBQK2R w KQkq - 0 5",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/2b1p3/1PB1P3/5N2/P1PP1PPP/RNBQK2R b KQkq - 0 4"
    },
    {
      "ply": 9,
      "san": "c3",
      "uci": "c2c3",
      "fen": "r1bqk1nr/pppp1ppp/2n5/4p3/1bB1P3/2P2N2/P2P1PPP/RNBQK2R b KQkq - 0 5",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/4p3/1bB1P3/5N2/P1PP1PPP/RNBQK2R w KQkq - 0 5"
    },
    {
      "ply": 10,
      "san": "Ba5",
      "uci": "b4a5",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b3p3/2B1P3/2P2N2/P2P1PPP/RNBQK2R w KQkq - 1 6",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/4p3/1bB1P3/2P2N2/P2P1PPP/RNBQK2R b KQkq - 0 5"
    },
    {
      "ply": 11,
      "san": "d4",
      "uci": "d2d4",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b3p3/2BPP3/2P2N2/P4PPP/RNBQK2R b KQkq - 0 6",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b3p3/2B1P3/2P2N2/P2P1PPP/RNBQK2R w KQkq - 1 6"
    },
    {
      "ply": 12,
      "san": "exd4",
      "uci": "e5d4",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b7/2BpP3/2P2N2/P4PPP/RNBQK2R w KQkq - 0 7",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b3p3/2BPP3/2P2N2/P4PPP/RNBQK2R b KQkq - 0 6"
    },
    {
      "ply": 13,
      "san": "O-O",
      "uci": "e1g1",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b7/2BpP3/2P2N2/P4PPP/RNBQ1RK1 b kq - 1 7",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b7/2BpP3/2P2N2/P4PPP/RNBQK2R w KQkq - 0 7"
    },
    {
      "ply": 14,
      "san": "d3",
      "uci": "d4d3",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b7/2B1P3/2Pp1N2/P4PPP/RNBQ1RK1 w kq - 0 8",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b7/2BpP3/2P2N2/P4PPP/RNBQ1RK1 b kq - 1 7"
    },
    {
      "ply": 15,
      "san": "Qb3",
      "uci": "d1b3",
      "fen": "r1bqk1nr/pppp1ppp/2n5/b7/2B1P3/1QPp1N2/P4PPP/RNB2RK1 b kq - 1 8",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b7/2B1P3/2Pp1N2/P4PPP/RNBQ1RK1 w kq - 0 8"
    },
    {
      "ply": 16,
      "san": "Qf6",
      "uci": "d8f6",
      "fen": "r1b1k1nr/pppp1ppp/2n2q2/b7/2B1P3/1QPp1N2/P4PPP/RNB2RK1 w kq - 2 9",
      "fenBefore": "r1bqk1nr/pppp1ppp/2n5/b7/2B1P3/1QPp1N2/P4PPP/RNB2RK1 b kq - 1 8"
    },
    {
      "ply": 17,
      "san": "e5",
      "uci": "e4e5",
      "fen": "r1b1k1nr/pppp1ppp/2n2q2/b3P3/2B5/1QPp1N2/P4PPP/RNB2RK1 b kq - 0 9",
      "fenBefore": "r1b1k1nr/pppp1ppp/2n2q2/b7/2B1P3/1QPp1N2/P4PPP/RNB2RK1 w kq - 2 9"
    },
    {
      "ply": 18,
      "san": "Qg6",
      "uci": "f6g6",
      "fen": "r1b1k1nr/pppp1ppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB2RK1 w kq - 1 10",
      "fenBefore": "r1b1k1nr/pppp1ppp/2n2q2/b3P3/2B5/1QPp1N2/P4PPP/RNB2RK1 b kq - 0 9"
    },
    {
      "ply": 19,
      "san": "Re1",
      "uci": "f1e1",
      "fen": "r1b1k1nr/pppp1ppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB1R1K1 b kq - 2 10",
      "fenBefore": "r1b1k1nr/pppp1ppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB2RK1 w kq - 1 10"
    },
    {
      "ply": 20,
      "san": "Nge7",
      "uci": "g8e7",
      "fen": "r1b1k2r/ppppnppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB1R1K1 w kq - 3 11",
      "fenBefore": "r1b1k1nr/pppp1ppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB1R1K1 b kq - 2 10"
    },
    {
      "ply": 21,
      "san": "Ba3",
      "uci": "c1a3",
      "fen": "r1b1k2r/ppppnppp/2n3q1/b3P3/2B5/BQPp1N2/P4PPP/RN2R1K1 b kq - 4 11",
      "fenBefore": "r1b1k2r/ppppnppp/2n3q1/b3P3/2B5/1QPp1N2/P4PPP/RNB1R1K1 w kq - 3 11"
    },
    {
      "ply": 22,
      "san": "b5",
      "uci": "b7b5",
      "fen": "r1b1k2r/p1ppnppp/2n3q1/bp2P3/2B5/BQPp1N2/P4PPP/RN2R1K1 w kq - 0 12",
      "fenBefore": "r1b1k2r/ppppnppp/2n3q1/b3P3/2B5/BQPp1N2/P4PPP/RN2R1K1 b kq - 4 11"
    },
    {
      "ply": 23,
      "san": "Qxb5",
      "uci": "b3b5",
      "fen": "r1b1k2r/p1ppnppp/2n3q1/bQ2P3/2B5/B1Pp1N2/P4PPP/RN2R1K1 b kq - 0 12",
      "fenBefore": "r1b1k2r/p1ppnppp/2n3q1/bp2P3/2B5/BQPp1N2/P4PPP/RN2R1K1 w kq - 0 12"
    },
    {
      "ply": 24,
      "san": "Rb8",
      "uci": "a8b8",
      "fen": "1rb1k2r/p1ppnppp/2n3q1/bQ2P3/2B5/B1Pp1N2/P4PPP/RN2R1K1 w k - 1 13",
      "fenBefore": "r1b1k2r/p1ppnppp/2n3q1/bQ2P3/2B5/B1Pp1N2/P4PPP/RN2R1K1 b kq - 0 12"
    },
    {
      "ply": 25,
      "san": "Qa4",
      "uci": "b5a4",
      "fen": "1rb1k2r/p1ppnppp/2n3q1/b3P3/Q1B5/B1Pp1N2/P4PPP/RN2R1K1 b k - 2 13",
      "fenBefore": "1rb1k2r/p1ppnppp/2n3q1/bQ2P3/2B5/B1Pp1N2/P4PPP/RN2R1K1 w k - 1 13"
    },
    {
      "ply": 26,
      "san": "Bb6",
      "uci": "a5b6",
      "fen": "1rb1k2r/p1ppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P4PPP/RN2R1K1 w k - 3 14",
      "fenBefore": "1rb1k2r/p1ppnppp/2n3q1/b3P3/Q1B5/B1Pp1N2/P4PPP/RN2R1K1 b k - 2 13"
    },
    {
      "ply": 27,
      "san": "Nbd2",
      "uci": "b1d2",
      "fen": "1rb1k2r/p1ppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P2N1PPP/R3R1K1 b k - 4 14",
      "fenBefore": "1rb1k2r/p1ppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P4PPP/RN2R1K1 w k - 3 14"
    },
    {
      "ply": 28,
      "san": "Bb7",
      "uci": "c8b7",
      "fen": "1r2k2r/pbppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P2N1PPP/R3R1K1 w k - 5 15",
      "fenBefore": "1rb1k2r/p1ppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P2N1PPP/R3R1K1 b k - 4 14"
    },
    {
      "ply": 29,
      "san": "Ne4",
      "uci": "d2e4",
      "fen": "1r2k2r/pbppnppp/1bn3q1/4P3/Q1B1N3/B1Pp1N2/P4PPP/R3R1K1 b k - 6 15",
      "fenBefore": "1r2k2r/pbppnppp/1bn3q1/4P3/Q1B5/B1Pp1N2/P2N1PPP/R3R1K1 w k - 5 15"
    },
    {
      "ply": 30,
      "san": "Qf5",
      "uci": "g6f5",
      "fen": "1r2k2r/pbppnppp/1bn5/4Pq2/Q1B1N3/B1Pp1N2/P4PPP/R3R1K1 w k - 7 16",
      "fenBefore": "1r2k2r/pbppnppp/1bn3q1/4P3/Q1B1N3/B1Pp1N2/P4PPP/R3R1K1 b k - 6 15"
    },
    {
      "ply": 31,
      "san": "Bxd3",
      "uci": "c4d3",
      "fen": "1r2k2r/pbppnppp/1bn5/4Pq2/Q3N3/B1PB1N2/P4PPP/R3R1K1 b k - 0 16",
      "fenBefore": "1r2k2r/pbppnppp/1bn5/4Pq2/Q1B1N3/B1Pp1N2/P4PPP/R3R1K1 w k - 7 16"
    },
    {
      "ply": 32,
      "san": "Qh5",
      "uci": "f5h5",
      "fen": "1r2k2r/pbppnppp/1bn5/4P2q/Q3N3/B1PB1N2/P4PPP/R3R1K1 w k - 1 17",
      "fenBefore": "1r2k2r/pbppnppp/1bn5/4Pq2/Q3N3/B1PB1N2/P4PPP/R3R1K1 b k - 0 16"
    },
    {
      "ply": 33,
      "san": "Nf6+",
      "uci": "e4f6",
      "fen": "1r2k2r/pbppnppp/1bn2N2/4P2q/Q7/B1PB1N2/P4PPP/R3R1K1 b k - 2 17",
      "fenBefore": "1r2k2r/pbppnppp/1bn5/4P2q/Q3N3/B1PB1N2/P4PPP/R3R1K1 w k - 1 17"
    },
    {
      "ply": 34,
      "san": "gxf6",
      "uci": "g7f6",
      "fen": "1r2k2r/pbppnp1p/1bn2p2/4P2q/Q7/B1PB1N2/P4PPP/R3R1K1 w k - 0 18",
      "fenBefore": "1r2k2r/pbppnppp/1bn2N2/4P2q/Q7/B1PB1N2/P4PPP/R3R1K1 b k - 2 17"
    },
    {
      "ply": 35,
      "san": "exf6",
      "uci": "e5f6",
      "fen": "1r2k2r/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/R3R1K1 b k - 0 18",
      "fenBefore": "1r2k2r/pbppnp1p/1bn2p2/4P2q/Q7/B1PB1N2/P4PPP/R3R1K1 w k - 0 18"
    },
    {
      "ply": 36,
      "san": "Rg8",
      "uci": "h8g8",
      "fen": "1r2k1r1/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/R3R1K1 w - - 1 19",
      "fenBefore": "1r2k2r/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/R3R1K1 b k - 0 18"
    },
    {
      "ply": 37,
      "san": "Rad1",
      "uci": "a1d1",
      "fen": "1r2k1r1/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/3RR1K1 b - - 2 19",
      "fenBefore": "1r2k1r1/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/R3R1K1 w - - 1 19"
    },
    {
      "ply": 38,
      "san": "Qxf3",
      "uci": "h5f3",
      "fen": "1r2k1r1/pbppnp1p/1bn2P2/8/Q7/B1PB1q2/P4PPP/3RR1K1 w - - 0 20",
      "fenBefore": "1r2k1r1/pbppnp1p/1bn2P2/7q/Q7/B1PB1N2/P4PPP/3RR1K1 b - - 2 19"
    },
    {
      "ply": 39,
      "san": "Rxe7+",
      "uci": "e1e7",
      "fen": "1r2k1r1/pbppRp1p/1bn2P2/8/Q7/B1PB1q2/P4PPP/3R2K1 b - - 0 20",
      "fenBefore": "1r2k1r1/pbppnp1p/1bn2P2/8/Q7/B1PB1q2/P4PPP/3RR1K1 w - - 0 20"
    },
    {
      "ply": 40,
      "san": "Nxe7",
      "uci": "c6e7",
      "fen": "1r2k1r1/pbppnp1p/1b3P2/8/Q7/B1PB1q2/P4PPP/3R2K1 w - - 0 21",
      "fenBefore": "1r2k1r1/pbppRp1p/1bn2P2/8/Q7/B1PB1q2/P4PPP/3R2K1 b - - 0 20"
    },
    {
      "ply": 41,
      "san": "Qxd7+",
      "uci": "a4d7",
      "fen": "1r2k1r1/pbpQnp1p/1b3P2/8/8/B1PB1q2/P4PPP/3R2K1 b - - 0 21",
      "fenBefore": "1r2k1r1/pbppnp1p/1b3P2/8/Q7/B1PB1q2/P4PPP/3R2K1 w - - 0 21"
    },
    {
      "ply": 42,
      "san": "Kxd7",
      "uci": "e8d7",
      "fen": "1r4r1/pbpknp1p/1b3P2/8/8/B1PB1q2/P4PPP/3R2K1 w - - 0 22",
      "fenBefore": "1r2k1r1/pbpQnp1p/1b3P2/8/8/B1PB1q2/P4PPP/3R2K1 b - - 0 21"
    },
    {
      "ply": 43,
      "san": "Bf5+",
      "uci": "d3f5",
      "fen": "1r4r1/pbpknp1p/1b3P2/5B2/8/B1P2q2/P4PPP/3R2K1 b - - 1 22",
      "fenBefore": "1r4r1/pbpknp1p/1b3P2/8/8/B1PB1q2/P4PPP/3R2K1 w - - 0 22"
    },
    {
      "ply": 44,
      "san": "Ke8",
      "uci": "d7e8",
      "fen": "1r2k1r1/pbp1np1p/1b3P2/5B2/8/B1P2q2/P4PPP/3R2K1 w - - 2 23",
      "fenBefore": "1r4r1/pbpknp1p/1b3P2/5B2/8/B1P2q2/P4PPP/3R2K1 b - - 1 22"
    },
    {
      "ply": 45,
      "san": "Bd7+",
      "uci": "f5d7",
      "fen": "1r2k1r1/pbpBnp1p/1b3P2/8/8/B1P2q2/P4PPP/3R2K1 b - - 3 23",
      "fenBefore": "1r2k1r1/pbp1np1p/1b3P2/5B2/8/B1P2q2/P4PPP/3R2K1 w - - 2 23"
    },
    {
      "ply": 46,
      "san": "Kf8",
      "uci": "e8f8",
      "fen": "1r3kr1/pbpBnp1p/1b3P2/8/8/B1P2q2/P4PPP/3R2K1 w - - 4 24",
      "fenBefore": "1r2k1r1/pbpBnp1p/1b3P2/8/8/B1P2q2/P4PPP/3R2K1 b - - 3 23"
    },
    {
      "ply": 47,
      "san": "Bxe7#",
      "uci": "a3e7",
      "fen": "1r3kr1/pbpBBp1p/1b3P2/8/8/2P2q2/P4PPP/3R2K1 b - - 0 24",
      "fenBefore": "1r3kr1/pbpBnp1p/1b3P2/8/8/B1P2q2/P4PPP/3R2K1 w - - 4 24"
    }
  ],
  "points": [
    {
      "ply": 0,
      "cp": 0,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 1,
      "cp": 25,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 2,
      "cp": 20,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 3,
      "cp": 30,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 4,
      "cp": 28,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 5,
      "cp": 38,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 6,
      "cp": 30,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 7,
      "cp": -30,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 8,
      "cp": -25,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 9,
      "cp": -15,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 10,
      "cp": -5,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 11,
      "cp": 25,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 12,
      "cp": 35,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 13,
      "cp": 45,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 14,
      "cp": 55,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 15,
      "cp": 70,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 16,
      "cp": 55,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 17,
      "cp": 65,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 18,
      "cp": 50,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 19,
      "cp": 75,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 20,
      "cp": 70,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 21,
      "cp": 85,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 22,
      "cp": 60,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 23,
      "cp": 90,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 24,
      "cp": 85,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 25,
      "cp": 95,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 26,
      "cp": 88,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 27,
      "cp": 100,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 28,
      "cp": 95,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 29,
      "cp": 115,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 30,
      "cp": 290,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 31,
      "cp": -10,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 32,
      "cp": -5,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 33,
      "cp": -145,
      "mate": null,
      "depth": 21
    },
    {
      "ply": 34,
      "cp": -175,
      "mate": null,
      "depth": 22
    },
    {
      "ply": 35,
      "cp": -165,
      "mate": null,
      "depth": 23
    },
    {
      "ply": 36,
      "cp": 175,
      "mate": null,
      "depth": 18
    },
    {
      "ply": 37,
      "cp": 170,
      "mate": null,
      "depth": 19
    },
    {
      "ply": 38,
      "cp": -130,
      "mate": null,
      "depth": 20
    },
    {
      "ply": 39,
      "cp": null,
      "mate": 5,
      "depth": 22
    },
    {
      "ply": 40,
      "cp": null,
      "mate": 4,
      "depth": 23
    },
    {
      "ply": 41,
      "cp": null,
      "mate": 3,
      "depth": 24
    },
    {
      "ply": 42,
      "cp": null,
      "mate": 2,
      "depth": 22
    },
    {
      "ply": 43,
      "cp": null,
      "mate": 2,
      "depth": 23
    },
    {
      "ply": 44,
      "cp": null,
      "mate": 1,
      "depth": 24
    },
    {
      "ply": 45,
      "cp": null,
      "mate": 1,
      "depth": 22
    },
    {
      "ply": 46,
      "cp": null,
      "mate": 0,
      "depth": 23
    },
    {
      "ply": 47,
      "cp": null,
      "mate": 0,
      "depth": 24
    }
  ],
  "classifications": [
    {
      "ply": 7,
      "key": "mc-inaccuracy",
      "label": "失误",
      "lossCp": 68,
      "note": "贪吃弃兵,把中心让给了黑方",
      "bestUci": "d2d4",
      "bestSan": "d4",
      "line": [
        "d2d4",
        "e5d4",
        "c2c3",
        "d4c3",
        "b1c3"
      ],
      "lineSan": [
        "d4",
        "exd4",
        "c3",
        "dxc3",
        "Nxc3"
      ]
    },
    {
      "ply": 14,
      "key": "mc-good",
      "label": "良好",
      "lossCp": 40,
      "note": "",
      "bestUci": null,
      "bestSan": null,
      "line": [],
      "lineSan": []
    },
    {
      "ply": 19,
      "key": "mc-good",
      "label": "良好",
      "lossCp": 45,
      "note": "",
      "bestUci": null,
      "bestSan": null,
      "line": [],
      "lineSan": []
    },
    {
      "ply": 23,
      "key": "mc-excellent",
      "label": "优秀",
      "lossCp": 15,
      "note": "",
      "bestUci": null,
      "bestSan": null,
      "line": [],
      "lineSan": []
    },
    {
      "ply": 29,
      "key": "mc-excellent",
      "label": "优秀",
      "lossCp": 18,
      "note": "",
      "bestUci": null,
      "bestSan": null,
      "line": [],
      "lineSan": []
    },
    {
      "ply": 30,
      "key": "mc-mistake",
      "label": "错误",
      "lossCp": 175,
      "note": "黑方让出了对 e3 格的牵制",
      "bestUci": "g6f6",
      "bestSan": "Qf6",
      "line": [
        "g6f6",
        "e4d6",
        "e8f8",
        "c4f7"
      ],
      "lineSan": [
        "Qf6",
        "Nd6+",
        "Kf8",
        "Bxf7"
      ]
    },
    {
      "ply": 31,
      "key": "mc-missed",
      "label": "漏着",
      "lossCp": 300,
      "note": "本来在赢,却把胜势送掉了",
      "bestUci": "e4g5",
      "bestSan": "Neg5",
      "line": [
        "e4g5",
        "h7h6",
        "g5e6",
        "f7e6"
      ],
      "lineSan": [
        "Neg5",
        "h6",
        "Ne6",
        "fxe6"
      ]
    },
    {
      "ply": 33,
      "key": "mc-mistake",
      "label": "错误",
      "lossCp": 140,
      "note": "弃马换势,但这步之后没有后续攻击",
      "bestUci": "f3g5",
      "bestSan": "Nfg5",
      "line": [
        "f3g5",
        "h5h6",
        "g5e6",
        "f7e6"
      ],
      "lineSan": [
        "Nfg5",
        "Qh6",
        "Ne6",
        "fxe6"
      ]
    },
    {
      "ply": 36,
      "key": "mc-blunder",
      "label": "严重失误",
      "lossCp": 340,
      "note": "黑方错过了唯一的防守",
      "bestUci": "h5e5",
      "bestSan": "Qe5",
      "line": [
        "h5e5",
        "a1d1",
        "e8f8",
        "f3h4"
      ],
      "lineSan": [
        "Qe5",
        "Rad1",
        "Kf8",
        "Nh4"
      ]
    }
  ]
};
