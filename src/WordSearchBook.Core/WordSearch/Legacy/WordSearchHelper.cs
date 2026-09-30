using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace WordSearchHelper
{
    public class Letter
    {
        public int X { get; set; }
        public int Y { get; set; }
        public char _Letter { get; set; }
        public Letter(char letter, Point p )
        {
            this._Letter = letter;
            this.X = p.X;
            this.Y = p.Y;
        }
    }

    public enum Direction
    {
        S = 0,
        Se,
        Sw,
        N,
        Ne,
        Nw,
        E,
        W
    }

    public class WordBank
    {
        private char[,] lastgrid;
        private int lastlength;
        private int lastheight;

        public Letter[][] answers;
        private int uppercase;
        private Random rand;

        public string[] Words { get; set; }

        private char Rnd()
        {
            int ascii = rand.Next(0, 26);
            return (char)(uppercase + ascii);
        }

        private Direction[] AllDirections()
        {
            List<Direction> dirs = new List<Direction>();

            for (int i = 0; i < 8; i++)
                dirs.Add((Direction)i);

            return dirs.ToArray();
        }

        private Point[] AllPoints(int width,int height)
        {
            List<Point> all = new List<Point>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    all.Add(new Point(x, y));
                }
            }

            return all.ToArray();
        }

        private Point LogicalPlace(Direction dir, int wordlength, int length)
        {
            List<Point> points = AllPoints(length, length).ToList();
            while (points.Count > 0)
            {
                int index = rand.Next(0, points.Count);
                Point p = Add(points[index], dir, wordlength - 1);
                if (p.X >= 0 && p.X < length && p.Y >= 0 && p.Y < length)
                    return points[index];
                points.RemoveAt(index);
            }
            throw new Exception("No places left.");
        }

        private Point Add(Point p, Direction dir, int length)
        {
            Point last = p;
            for (int i = 0; i < length; i++)
            {
                switch (dir)
                {
                    case Direction.E:
                        last.X++;
                        break;
                    case Direction.N:
                        last.Y--;
                        break;
                    case Direction.Ne:
                        last.Y--;
                        last.X++;
                        break;
                    case Direction.Nw:
                        last.Y--;
                        last.X--;
                        break;
                    case Direction.S:
                        last.Y++;
                        break;
                    case Direction.Se:
                        last.Y++;
                        last.X++;
                        break;
                    case Direction.Sw:
                        last.Y++;
                        last.X--;
                        break;
                    case Direction.W:
                        last.X--;
                        break;
                }
            }
            return last;
        }

        private Point[] ParentNodes(List<Point> seeds, Point node, int Width, int Height)
        {
            List<Point> parents = new List<Point>();
            if (node.X - 1 > -1)
            {
                Point n = new Point(node.X - 1, node.Y);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.X - 1 > -1 && node.Y - 1 > -1)
            {
                Point n = new Point(node.X - 1, node.Y - 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.Y - 1 > -1)
            {
                Point n = new Point(node.X, node.Y - 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.X + 1 < Width && node.Y - 1 > -1)
            {
                Point n = new Point(node.X + 1, node.Y - 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.X + 1 < Width)
            {
                Point n = new Point(node.X + 1, node.Y);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.X + 1 < Width && node.Y + 1 < Height)
            {
                Point n = new Point(node.X + 1, node.Y + 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.Y + 1 < Height)
            {
                Point n = new Point(node.X, node.Y + 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            if (node.X - 1 > -1 && node.Y + 1 < Height)
            {
                Point n = new Point(node.X - 1, node.Y + 1);
                if (!seeds.Contains(n))
                    parents.Add(n);
            }
            return parents.ToArray();
        }

        private Letter[] Random(string word, int length)
        {
            Direction random = (Direction)rand.Next(0, 8);
            Point p = LogicalPlace(random, word.Length, length);
            return Construct(p, random, word);
        }

        private Letter[] Construct(Point p, Direction dir, string word)
        {
            Letter[] array = new Letter[word.Length];
            Point last = p;
            for (int i = 0; i < word.Length; i++)
            {
                array[i] = new Letter(word[i], last);
                switch (dir)
                {
                    case Direction.E:
                        last.X++;
                        break;
                    case Direction.N:
                        last.Y--;
                        break;
                    case Direction.Ne:
                        last.Y--;
                        last.X++;
                        break;
                    case Direction.Nw:
                        last.Y--;
                        last.X--;
                        break;
                    case Direction.S:
                        last.Y++;
                        break;
                    case Direction.Se:
                        last.Y++;
                        last.X++;
                        break;
                    case Direction.Sw:
                        last.Y++;
                        last.X--;
                        break;
                    case Direction.W:
                        last.X--;
                        break;
                }
            }
            return array;
        }

        private Letter[] Next(Letter[][] letters, string word, int width, int height)
        {
            List<Point> all = new List<Point>(AllPoints(width, height));
            while (all.Count > 0)
            {
                int index = rand.Next(0, all.Count);
                List<Direction> dirs = new List<Direction>(AllDirections());
                while (dirs.Count > 0)
                {
                    int index2 = rand.Next(0, dirs.Count);
                    Direction current = dirs[index2];
                    Letter[] c = Construct(all[index], current, word);
                    bool legal = true;
                    for (int i = 0; i < c.Length; i++)
                    {
                        if (c[i].X < 0 || c[i].X >= width || c[i].Y < 0 || c[i].Y >= height)
                        {
                            legal = false;
                            break;
                        }
                        else
                        {
                            for (int j = 0; j < letters.Length; j++)
                            {
                                Point[] pts = letters[j].Select<Letter, Point>(t => new Point(t.X, t.Y)).ToArray();
                                int _index2 = pts.ToList().FindIndex(t => t.X == c[i].X && t.Y == c[i].Y);
                                if (_index2 != -1)
                                    if (c[i]._Letter != letters[j][_index2]._Letter)
                                        legal = false;
                            }
                        }
                    }
                    if (legal)
                        return c;
                    dirs.RemoveAt(index2);
                }
                all.RemoveAt(index);
            }
            return null;
        }

        private char[,] From(Letter[][] grid, int width,int height)
        {
            char[,] _grid = new char[width, height];
            for (int j = 0; j < grid.Length; j++)
            {
                Letter[] current = grid[j];
                for (int i = 0; i < current.Length; i++)
                {
                    _grid[current[i].X, current[i].Y] = current[i]._Letter;
                }
            }
            return _grid;
        }

        private char[,] Fill(char[,] current)
        {
            for (int x = 0; x < current.GetLength(0); x++)
            {
                for (int y = 0; y < current.GetLength(1); y++)
                {
                    if (current[x, y] == '\0')
                        current[x, y] = Rnd();
                }
            }
            return current;
        }

        public void Reseed()
        {
            rand = new Random();
        }

        public char[,] AnswerKey()
        {
            if (answers != null)
            {
                return From(answers, lastlength,lastheight);
            }
            return null;
        }

        public char[,] Generate(int width,int height)
        {
            int _min = width >= height ? height : width;
            if (_min < Words.Max(t => t.Length)) throw new Exception("Length Small");

            int _try = 20;

            while (true)
            {
                List<string> left = new List<string>(Words);
                List<Letter[]> words = new List<Letter[]>();
                bool notfound = false;
                while (left.Count > 0)
                {
                    Letter[] next = Next(words.ToArray(), left[left.Count - 1], width, height);
                    if (next == null)
                    {
                        notfound = true;
                        break;
                    }

                    words.Add(next);
                    left.RemoveAt(left.Count - 1);
                }

                if (!notfound)
                {
                    char[,] without = From(words.ToArray(), width,height);
                    this.lastlength = width;
                    this.lastheight = height;
                    this.answers = words.ToArray(); //Don't forget to set answer key!
                    this.lastgrid = without; //Don't forget to set lastgrid!
                    return Fill(without);
                }

                _try -= 1;
                if (_try < 0) throw new Exception($"setup grid fail input:{Words.Count()} setup:{words.Count()}");                    
            }
        }

        public WordBank(string[] words, bool uppercase)
        {
            this.Words = (uppercase) ? words.Select<string, string>(t => t.ToUpper()).ToArray() : words.Select<string, string>(t => t.ToLower()).ToArray();
            this.uppercase = uppercase ? 65 : 97;
            this.Reseed();
        }
    }
}
