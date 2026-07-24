using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class ColorSortLevelBuilder
{
    private const int FirstBlueBoardIndex = 16;
    private const int ThreeBlueBoardIndex = 25;
    private const int FourBlueBoardIndex = 35;
    private const int FirstYellowBoardIndex = 45;
    private const int ThreeYellowBoardIndex = 48;
    private const int FourYellowBoardIndex = 49;
    private const int FirstFiveByFiveBoardIndex = 50;
    private const int EarlyBlueMatchTarget = 2;
    private const int MiddleBlueMatchTarget = 3;
    private const int LateBlueMatchTarget = 4;
    private const int EarlyYellowMatchTarget = 2;
    private const int MiddleYellowMatchTarget = 3;
    private const int LateYellowMatchTarget = 4;
    private const int MaxGenerationAttempts = 5000;
    private const int MaxColorSequenceAttempts = 800;
    private const char EmptyPark = '-';

    private static readonly UnityGameManager.Direction[] AllDirections =
    {
        UnityGameManager.Direction.Up,
        UnityGameManager.Direction.Down,
        UnityGameManager.Direction.Left,
        UnityGameManager.Direction.Right
    };

    private struct RemovalOption
    {
        public int index;
        public UnityGameManager.Direction direction;
    }

    private struct TrayState
    {
        public string tray;
        public char park;
        public bool redCleared;
        public bool greenCleared;
        public bool blueCleared;
        public bool yellowCleared;

        public string Key()
        {
            return tray + "|" + park + "|" +
                   (redCleared ? "1" : "0") +
                   (greenCleared ? "1" : "0") +
                   (blueCleared ? "1" : "0") +
                   (yellowCleared ? "1" : "0");
        }
    }

    public static List<UnityGameManager.LevelConfig> BuildLevels(int count)
    {
        var levels = new List<UnityGameManager.LevelConfig>();
        var canonicalShapes = new HashSet<string>();
        var acceptedLayouts = new List<string>();

        for (int i = 0; i < count; i++)
        {
            levels.Add(BuildLevel(i, canonicalShapes, acceptedLayouts));
        }

        return levels;
    }

    public static UnityGameManager.LevelConfig BuildLevel(int boardIndex)
    {
        return BuildLevel(boardIndex, null, null);
    }

    private static UnityGameManager.LevelConfig BuildLevel(int boardIndex, HashSet<string> canonicalShapes, List<string> acceptedLayouts)
    {
        bool usesBlue = boardIndex >= FirstBlueBoardIndex;
        int blueMatchTarget = usesBlue ? GetBlueMatchTarget(boardIndex) : 0;
        int yellowMatchTarget = GetYellowMatchTarget(boardIndex);
        int size = GetBoardSize(boardIndex, usesBlue, yellowMatchTarget);
        int matchTarget = size;

        for (int attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var rng = new System.Random(SeedFor(boardIndex, attempt));

            if (!TryBuildRemovalPlan(size, rng, out int[] removalOrder, out UnityGameManager.Direction[] directions))
            {
                continue;
            }

            if (!TryBuildColorSequence(boardIndex, size, matchTarget, blueMatchTarget, yellowMatchTarget, rng, out List<UnityGameManager.BlockColor> colorSequence))
            {
                continue;
            }

            UnityGameManager.LevelConfig level = CreateLevelFromPlan(boardIndex, size, matchTarget, removalOrder, directions, colorSequence);
            string rawLayout = ColorLayoutKey(level.blocks, size);
            string canonicalLayout = CanonicalColorLayout(rawLayout, size);

            if (canonicalShapes != null && canonicalShapes.Contains(canonicalLayout))
            {
                continue;
            }

            if (acceptedLayouts != null && IsTooSimilarToAccepted(rawLayout, size, acceptedLayouts))
            {
                continue;
            }

            if (!HasHealthyColorMix(level.blocks, size))
            {
                continue;
            }

            if (!HasHealthyDirectionMix(level.blocks, size))
            {
                continue;
            }

            if (HasFacingArrows(level.blocks, size))
            {
                continue;
            }

            if (!ValidatePlannedSolution(level.blocks, size, removalOrder))
            {
                continue;
            }

            if (!IsTraySequenceSolvable(colorSequence, matchTarget, blueMatchTarget, yellowMatchTarget))
            {
                continue;
            }

            canonicalShapes?.Add(canonicalLayout);
            acceptedLayouts?.Add(rawLayout);
            return level;
        }

        Debug.LogWarning($"Could not generate a fully unique level for Board {boardIndex + 1}; using a relaxed validated fallback.");
        return BuildFallbackLevel(boardIndex);
    }

    private static int SeedFor(int boardIndex, int attempt)
    {
        unchecked
        {
            return 17391 + boardIndex * 92821 + attempt * 131071;
        }
    }

    private static bool TryBuildRemovalPlan(int size, System.Random rng, out int[] removalOrder, out UnityGameManager.Direction[] directions)
    {
        int count = size * size;
        removalOrder = new int[count];
        directions = new UnityGameManager.Direction[count];

        int remainingMask = (1 << count) - 1;
        int[] directionCounts = new int[AllDirections.Length];
        UnityGameManager.Direction? previousDirection = null;

        for (int step = 0; step < count; step++)
        {
            var options = new List<RemovalOption>();

            for (int index = 0; index < count; index++)
            {
                if ((remainingMask & (1 << index)) == 0) continue;

                foreach (UnityGameManager.Direction direction in AllDirections)
                {
                    if (IsPathClear(index, direction, remainingMask, size))
                    {
                        options.Add(new RemovalOption { index = index, direction = direction });
                    }
                }
            }

            if (options.Count == 0) return false;

            int bestScore = int.MinValue;
            var bestOptions = new List<RemovalOption>();
            foreach (RemovalOption option in options)
            {
                int score = rng.Next(0, 7);
                int directionIndex = DirectionIndex(option.direction);
                score -= directionCounts[directionIndex] * 5;

                if (previousDirection.HasValue && previousDirection.Value == option.direction)
                {
                    score -= 4;
                }

                if (WouldFaceAssignedNeighbor(option.index, option.direction, directions, removalOrder, step, size))
                {
                    score -= 12;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOptions.Clear();
                    bestOptions.Add(option);
                }
                else if (score == bestScore)
                {
                    bestOptions.Add(option);
                }
            }

            RemovalOption chosen = bestOptions[rng.Next(bestOptions.Count)];
            removalOrder[step] = chosen.index;
            directions[chosen.index] = chosen.direction;
            directionCounts[DirectionIndex(chosen.direction)]++;
            previousDirection = chosen.direction;
            remainingMask &= ~(1 << chosen.index);
        }

        return true;
    }

    private static bool TryBuildColorSequence(int boardIndex, int size, int matchTarget, int blueMatchTarget, int yellowMatchTarget, System.Random rng, out List<UnityGameManager.BlockColor> sequence)
    {
        if (size >= 5)
        {
            sequence = BuildFiveByFiveColorSequence(boardIndex, rng);
            return IsTraySequenceSolvable(sequence, matchTarget, blueMatchTarget, yellowMatchTarget) &&
                   !IsTraySequenceSolvableWithoutParking(sequence, matchTarget, blueMatchTarget, yellowMatchTarget);
        }

        sequence = null;
        var colors = BuildColorBag(size, blueMatchTarget, yellowMatchTarget);
        bool shouldNeedParking = boardIndex > 0;

        for (int attempt = 0; attempt < MaxColorSequenceAttempts; attempt++)
        {
            var candidate = new List<UnityGameManager.BlockColor>(colors);
            Shuffle(candidate, rng);

            if (!HasUsefulNeutralTiming(candidate, size))
            {
                continue;
            }

            if (shouldNeedParking && IsTraySequenceSolvableWithoutParking(candidate, matchTarget, blueMatchTarget, yellowMatchTarget))
            {
                continue;
            }

            if (!IsTraySequenceSolvable(candidate, matchTarget, blueMatchTarget, yellowMatchTarget))
            {
                continue;
            }

            sequence = candidate;
            return true;
        }

        return false;
    }

    private static List<UnityGameManager.BlockColor> BuildFiveByFiveColorSequence(int boardIndex, System.Random rng)
    {
        var colored = new List<UnityGameManager.BlockColor>(18);
        bool greenFirst = (boardIndex & 1) != 0;
        UnityGameManager.BlockColor first = greenFirst ? UnityGameManager.BlockColor.Green : UnityGameManager.BlockColor.Red;
        UnityGameManager.BlockColor second = greenFirst ? UnityGameManager.BlockColor.Red : UnityGameManager.BlockColor.Green;

        // Four matching cars, one interruption, then the fifth matching car
        // guarantees that the side parking bay is genuinely needed.
        AddColors(colored, first, 4);
        colored.Add(second);
        colored.Add(first);
        AddColors(colored, second, 4);

        // Alternate the two optional-color groups so consecutive late boards
        // do not have the same matching rhythm. Blue remains consecutive as
        // required by its special matching rule.
        if ((boardIndex & 2) == 0)
        {
            AddColors(colored, UnityGameManager.BlockColor.Blue, 4);
            AddColors(colored, UnityGameManager.BlockColor.Yellow, 4);
        }
        else
        {
            AddColors(colored, UnityGameManager.BlockColor.Yellow, 4);
            AddColors(colored, UnityGameManager.BlockColor.Blue, 4);
        }

        // Neutral police blocks never enter the matching tray. Distributing
        // them throughout the known-solvable color order creates varied board
        // pacing without changing the proof that every level can be finished.
        var neutralSlots = new HashSet<int> { 1, 13, 22 };
        while (neutralSlots.Count < 7)
            neutralSlots.Add(rng.Next(0, 25));

        var sequence = new List<UnityGameManager.BlockColor>(25);
        int coloredIndex = 0;
        for (int slot = 0; slot < 25; slot++)
        {
            sequence.Add(neutralSlots.Contains(slot)
                ? UnityGameManager.BlockColor.Neutral
                : colored[coloredIndex++]);
        }

        return sequence;
    }

    private static List<UnityGameManager.BlockColor> BuildColorBag(int size, int blueMatchTarget, int yellowMatchTarget)
    {
        var colors = new List<UnityGameManager.BlockColor>();
        int redCount = size;
        int greenCount = size;
        int blueCount = Mathf.Max(0, blueMatchTarget);
        int yellowCount = Mathf.Max(0, yellowMatchTarget);
        int neutralCount = size * size - redCount - greenCount - blueCount - yellowCount;

        AddColors(colors, UnityGameManager.BlockColor.Red, redCount);
        AddColors(colors, UnityGameManager.BlockColor.Green, greenCount);
        AddColors(colors, UnityGameManager.BlockColor.Blue, blueCount);
        AddColors(colors, UnityGameManager.BlockColor.Yellow, yellowCount);
        AddColors(colors, UnityGameManager.BlockColor.Neutral, neutralCount);
        return colors;
    }

    private static void AddColors(List<UnityGameManager.BlockColor> colors, UnityGameManager.BlockColor color, int count)
    {
        for (int i = 0; i < count; i++)
        {
            colors.Add(color);
        }
    }

    private static UnityGameManager.LevelConfig CreateLevelFromPlan(int boardIndex, int size, int matchTarget, int[] removalOrder, UnityGameManager.Direction[] directions, List<UnityGameManager.BlockColor> colorSequence)
    {
        var blockByIndex = new UnityGameManager.BlockData[size * size];

        for (int step = 0; step < removalOrder.Length; step++)
        {
            int index = removalOrder[step];
            UnityGameManager.BlockColor color = colorSequence[step];
            blockByIndex[index] = new UnityGameManager.BlockData
            {
                row = index / size,
                col = index % size,
                color = color,
                direction = directions[index],
                textLabel = LabelForColor(color)
            };
        }

        return new UnityGameManager.LevelConfig
        {
            id = boardIndex + 1,
            name = "Board " + (boardIndex + 1),
            boardSize = size,
            matchTarget = matchTarget,
            blocks = new List<UnityGameManager.BlockData>(blockByIndex)
        };
    }

    private static bool ValidatePlannedSolution(List<UnityGameManager.BlockData> blocks, int size, int[] removalOrder)
    {
        int remainingMask = (1 << (size * size)) - 1;
        var byIndex = new UnityGameManager.BlockData[size * size];

        foreach (UnityGameManager.BlockData block in blocks)
        {
            byIndex[block.row * size + block.col] = block;
        }

        foreach (int index in removalOrder)
        {
            UnityGameManager.BlockData block = byIndex[index];
            if (!IsPathClear(index, block.direction, remainingMask, size))
            {
                return false;
            }

            remainingMask &= ~(1 << index);
        }

        return true;
    }

    private static bool IsPathClear(int index, UnityGameManager.Direction direction, int remainingMask, int size)
    {
        int row = index / size;
        int col = index % size;

        if (direction == UnityGameManager.Direction.Up)
        {
            for (int r = row - 1; r >= 0; r--)
            {
                if ((remainingMask & (1 << (r * size + col))) != 0) return false;
            }
        }
        else if (direction == UnityGameManager.Direction.Down)
        {
            for (int r = row + 1; r < size; r++)
            {
                if ((remainingMask & (1 << (r * size + col))) != 0) return false;
            }
        }
        else if (direction == UnityGameManager.Direction.Left)
        {
            for (int c = col - 1; c >= 0; c--)
            {
                if ((remainingMask & (1 << (row * size + c))) != 0) return false;
            }
        }
        else if (direction == UnityGameManager.Direction.Right)
        {
            for (int c = col + 1; c < size; c++)
            {
                if ((remainingMask & (1 << (row * size + c))) != 0) return false;
            }
        }

        return true;
    }

    private static bool IsTraySequenceSolvable(List<UnityGameManager.BlockColor> sequence, int capacity, int blueMatchTarget, int yellowMatchTarget)
    {
        bool usesBlue = blueMatchTarget > 0;
        bool usesYellow = yellowMatchTarget > 0;
        var states = new HashSet<string>();
        var stateList = new List<TrayState>();
        TrayState start = NormalizeState(new TrayState { tray = string.Empty, park = EmptyPark }, capacity, blueMatchTarget, yellowMatchTarget);
        states.Add(start.Key());
        stateList.Add(start);

        foreach (UnityGameManager.BlockColor color in sequence)
        {
            if (color == UnityGameManager.BlockColor.Neutral) continue;

            stateList = ExpandParkingClosure(stateList, capacity, blueMatchTarget, yellowMatchTarget);
            var nextKeys = new HashSet<string>();
            var nextStates = new List<TrayState>();
            char colorChar = ColorChar(color);

            foreach (TrayState state in stateList)
            {
                if (state.tray.Length >= capacity) continue;

                TrayState added = state;
                added.tray += colorChar;
                added = NormalizeState(added, capacity, blueMatchTarget, yellowMatchTarget);
                if (nextKeys.Add(added.Key()))
                {
                    nextStates.Add(added);
                }
            }

            if (nextStates.Count == 0) return false;

            stateList = nextStates;
            states.Clear();
            foreach (TrayState state in stateList)
            {
                states.Add(state.Key());
            }
        }

        stateList = ExpandParkingClosure(stateList, capacity, blueMatchTarget, yellowMatchTarget);
        foreach (TrayState state in stateList)
        {
            if (state.redCleared && state.greenCleared && (!usesBlue || state.blueCleared) && (!usesYellow || state.yellowCleared))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTraySequenceSolvableWithoutParking(List<UnityGameManager.BlockColor> sequence, int capacity, int blueMatchTarget, int yellowMatchTarget)
    {
        bool usesBlue = blueMatchTarget > 0;
        bool usesYellow = yellowMatchTarget > 0;
        TrayState state = NormalizeState(new TrayState { tray = string.Empty, park = EmptyPark }, capacity, blueMatchTarget, yellowMatchTarget);

        foreach (UnityGameManager.BlockColor color in sequence)
        {
            if (color == UnityGameManager.BlockColor.Neutral) continue;
            if (state.tray.Length >= capacity) return false;

            state.tray += ColorChar(color);
            state = NormalizeState(state, capacity, blueMatchTarget, yellowMatchTarget);
        }

        return state.redCleared && state.greenCleared && (!usesBlue || state.blueCleared) && (!usesYellow || state.yellowCleared);
    }

    private static List<TrayState> ExpandParkingClosure(List<TrayState> startStates, int capacity, int blueMatchTarget, int yellowMatchTarget)
    {
        var queue = new Queue<TrayState>();
        var visited = new HashSet<string>();
        var result = new List<TrayState>();

        foreach (TrayState state in startStates)
        {
            TrayState normalized = NormalizeState(state, capacity, blueMatchTarget, yellowMatchTarget);
            if (visited.Add(normalized.Key()))
            {
                queue.Enqueue(normalized);
                result.Add(normalized);
            }
        }

        while (queue.Count > 0)
        {
            TrayState state = queue.Dequeue();

            if (state.park == EmptyPark)
            {
                for (int i = 0; i < state.tray.Length; i++)
                {
                    TrayState moved = state;
                    moved.park = state.tray[i];
                    moved.tray = state.tray.Remove(i, 1);
                    moved = NormalizeState(moved, capacity, blueMatchTarget, yellowMatchTarget);
                    if (visited.Add(moved.Key()))
                    {
                        queue.Enqueue(moved);
                        result.Add(moved);
                    }
                }
            }
            else
            {
                if (state.tray.Length < capacity)
                {
                    TrayState returned = state;
                    returned.tray += state.park;
                    returned.park = EmptyPark;
                    returned = NormalizeState(returned, capacity, blueMatchTarget, yellowMatchTarget);
                    if (visited.Add(returned.Key()))
                    {
                        queue.Enqueue(returned);
                        result.Add(returned);
                    }
                }

                for (int i = 0; i < state.tray.Length; i++)
                {
                    char[] chars = state.tray.ToCharArray();
                    char oldTrayChar = chars[i];
                    chars[i] = state.park;

                    TrayState swapped = state;
                    swapped.tray = new string(chars);
                    swapped.park = oldTrayChar;
                    swapped = NormalizeState(swapped, capacity, blueMatchTarget, yellowMatchTarget);
                    if (visited.Add(swapped.Key()))
                    {
                        queue.Enqueue(swapped);
                        result.Add(swapped);
                    }
                }
            }
        }

        return result;
    }

    private static TrayState NormalizeState(TrayState state, int matchTarget, int blueMatchTarget, int yellowMatchTarget)
    {
        bool usesBlue = blueMatchTarget > 0;
        bool usesYellow = yellowMatchTarget > 0;
        bool changed = true;
        while (changed)
        {
            changed = false;

            if (!state.redCleared && CountChar(state.tray, 'R') >= matchTarget)
            {
                state.tray = RemoveFirstCount(state.tray, 'R', matchTarget);
                state.redCleared = true;
                changed = true;
            }

            if (!state.greenCleared && CountChar(state.tray, 'G') >= matchTarget)
            {
                state.tray = RemoveFirstCount(state.tray, 'G', matchTarget);
                state.greenCleared = true;
                changed = true;
            }

            if (usesBlue && !state.blueCleared)
            {
                int blueRunIndex = FindAdjacentBlueRun(state.tray, blueMatchTarget);
                if (blueRunIndex >= 0)
                {
                    state.tray = state.tray.Remove(blueRunIndex, blueMatchTarget);
                    state.blueCleared = true;
                    changed = true;
                }
            }

            if (usesYellow && !state.yellowCleared && CountChar(state.tray, 'Y') >= yellowMatchTarget)
            {
                state.tray = RemoveFirstCount(state.tray, 'Y', yellowMatchTarget);
                state.yellowCleared = true;
                changed = true;
            }
        }

        return state;
    }

    private static int CountChar(string value, char target)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == target) count++;
        }

        return count;
    }

    private static string RemoveFirstCount(string value, char target, int count)
    {
        var builder = new StringBuilder();
        int removed = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == target && removed < count)
            {
                removed++;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static int FindAdjacentBlueRun(string tray, int target)
    {
        if (target <= 0) return -1;

        for (int i = 0; i <= tray.Length - target; i++)
        {
            bool matches = true;
            for (int offset = 0; offset < target; offset++)
            {
                if (tray[i + offset] != 'B')
                {
                    matches = false;
                    break;
                }
            }

            if (matches) return i;
        }

        return -1;
    }

    private static int GetBlueMatchTarget(int boardIndex)
    {
        if (boardIndex >= FourBlueBoardIndex) return LateBlueMatchTarget;
        if (boardIndex >= ThreeBlueBoardIndex) return MiddleBlueMatchTarget;
        return EarlyBlueMatchTarget;
    }

    private static int GetYellowMatchTarget(int boardIndex)
    {
        if (boardIndex >= FourYellowBoardIndex) return LateYellowMatchTarget;
        if (boardIndex >= ThreeYellowBoardIndex) return MiddleYellowMatchTarget;
        if (boardIndex >= FirstYellowBoardIndex) return EarlyYellowMatchTarget;
        return 0;
    }

    private static int GetBoardSize(int boardIndex, bool usesBlue, int yellowMatchTarget)
    {
        if (boardIndex >= FirstFiveByFiveBoardIndex) return 5;
        return boardIndex >= 5 || usesBlue || yellowMatchTarget > 0 ? 4 : 3;
    }

    private static bool HasUsefulNeutralTiming(List<UnityGameManager.BlockColor> sequence, int size)
    {
        int firstNeutral = -1;
        int lastNeutral = -1;
        int neutralCount = 0;
        for (int i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] != UnityGameManager.BlockColor.Neutral) continue;
            neutralCount++;
            if (firstNeutral < 0) firstNeutral = i;
            lastNeutral = i;
        }

        if (neutralCount == 0) return true;
        if (neutralCount <= 2) return lastNeutral >= size;
        return firstNeutral >= 0 && firstNeutral <= size && lastNeutral >= sequence.Count / 2;
    }

    private static bool HasHealthyColorMix(List<UnityGameManager.BlockData> blocks, int size)
    {
        var colors = ToColorGrid(blocks, size);

        for (int r = 0; r < size; r++)
        {
            if (HasLineClump(colors, size, r, 0, 0, 1)) return false;
        }

        for (int c = 0; c < size; c++)
        {
            if (HasLineClump(colors, size, 0, c, 1, 0)) return false;
        }

        return true;
    }

    private static bool HasLineClump(UnityGameManager.BlockColor[,] colors, int size, int startRow, int startCol, int rowStep, int colStep)
    {
        var counts = new Dictionary<UnityGameManager.BlockColor, int>();
        for (int i = 0; i < size; i++)
        {
            UnityGameManager.BlockColor color = colors[startRow + rowStep * i, startCol + colStep * i];
            if (!counts.ContainsKey(color)) counts[color] = 0;
            counts[color]++;
        }

        foreach (var pair in counts)
        {
            if (pair.Key == UnityGameManager.BlockColor.Neutral && pair.Value == size) return true;
            if (pair.Key != UnityGameManager.BlockColor.Neutral && pair.Value >= Mathf.Min(3, size)) return true;
        }

        return false;
    }

    private static bool HasHealthyDirectionMix(List<UnityGameManager.BlockData> blocks, int size)
    {
        var used = new HashSet<UnityGameManager.Direction>();
        var directionGrid = new UnityGameManager.Direction[size, size];

        foreach (UnityGameManager.BlockData block in blocks)
        {
            used.Add(block.direction);
            directionGrid[block.row, block.col] = block.direction;
        }

        int minimumDirections = size == 3 ? 3 : 4;
        if (used.Count < minimumDirections) return false;

        for (int r = 0; r < size; r++)
        {
            bool allSame = true;
            for (int c = 1; c < size; c++)
            {
                if (directionGrid[r, c] != directionGrid[r, 0]) allSame = false;
            }

            if (allSame) return false;
        }

        for (int c = 0; c < size; c++)
        {
            bool allSame = true;
            for (int r = 1; r < size; r++)
            {
                if (directionGrid[r, c] != directionGrid[0, c]) allSame = false;
            }

            if (allSame) return false;
        }

        return true;
    }

    private static bool HasFacingArrows(List<UnityGameManager.BlockData> blocks, int size)
    {
        var directionGrid = new UnityGameManager.Direction[size, size];

        foreach (UnityGameManager.BlockData block in blocks)
        {
            directionGrid[block.row, block.col] = block.direction;
        }

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (c + 1 < size &&
                    directionGrid[r, c] == UnityGameManager.Direction.Right &&
                    directionGrid[r, c + 1] == UnityGameManager.Direction.Left)
                {
                    return true;
                }

                if (r + 1 < size &&
                    directionGrid[r, c] == UnityGameManager.Direction.Down &&
                    directionGrid[r + 1, c] == UnityGameManager.Direction.Up)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool WouldFaceAssignedNeighbor(int index, UnityGameManager.Direction direction, UnityGameManager.Direction[] directions, int[] removalOrder, int assignedCount, int size)
    {
        int row = index / size;
        int col = index % size;

        for (int i = 0; i < assignedCount; i++)
        {
            int assignedIndex = removalOrder[i];
            int assignedRow = assignedIndex / size;
            int assignedCol = assignedIndex % size;
            UnityGameManager.Direction assignedDirection = directions[assignedIndex];

            if (row == assignedRow && col + 1 == assignedCol &&
                direction == UnityGameManager.Direction.Right &&
                assignedDirection == UnityGameManager.Direction.Left)
            {
                return true;
            }

            if (row == assignedRow && col - 1 == assignedCol &&
                direction == UnityGameManager.Direction.Left &&
                assignedDirection == UnityGameManager.Direction.Right)
            {
                return true;
            }

            if (row + 1 == assignedRow && col == assignedCol &&
                direction == UnityGameManager.Direction.Down &&
                assignedDirection == UnityGameManager.Direction.Up)
            {
                return true;
            }

            if (row - 1 == assignedRow && col == assignedCol &&
                direction == UnityGameManager.Direction.Up &&
                assignedDirection == UnityGameManager.Direction.Down)
            {
                return true;
            }
        }

        return false;
    }

    private static string ColorLayoutKey(List<UnityGameManager.BlockData> blocks, int size)
    {
        char[] chars = new char[size * size];
        foreach (UnityGameManager.BlockData block in blocks)
        {
            chars[block.row * size + block.col] = ColorChar(block.color);
        }

        return new string(chars);
    }

    private static string CanonicalColorLayout(string raw, int size)
    {
        string best = null;
        for (int transform = 0; transform < 8; transform++)
        {
            string transformed = TransformLayout(raw, size, transform);
            string swapped = SwapRedGreen(transformed);

            if (best == null || string.CompareOrdinal(transformed, best) < 0) best = transformed;
            if (string.CompareOrdinal(swapped, best) < 0) best = swapped;
        }

        return best;
    }

    private static bool IsTooSimilarToAccepted(string raw, int size, List<string> acceptedLayouts)
    {
        int allowedDifference = size == 3 ? 1 : 2;

        foreach (string previous in acceptedLayouts)
        {
            if (previous.Length != raw.Length) continue;

            for (int transform = 0; transform < 8; transform++)
            {
                string transformed = TransformLayout(raw, size, transform);
                if (HammingDistance(transformed, previous) <= allowedDifference) return true;

                string swapped = SwapRedGreen(transformed);
                if (HammingDistance(swapped, previous) <= allowedDifference) return true;
            }
        }

        return false;
    }

    private static string TransformLayout(string raw, int size, int transform)
    {
        char[] result = new char[raw.Length];

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                int sourceR = r;
                int sourceC = c;

                switch (transform)
                {
                    case 1:
                        sourceR = size - 1 - c;
                        sourceC = r;
                        break;
                    case 2:
                        sourceR = size - 1 - r;
                        sourceC = size - 1 - c;
                        break;
                    case 3:
                        sourceR = c;
                        sourceC = size - 1 - r;
                        break;
                    case 4:
                        sourceR = r;
                        sourceC = size - 1 - c;
                        break;
                    case 5:
                        sourceR = size - 1 - r;
                        sourceC = c;
                        break;
                    case 6:
                        sourceR = c;
                        sourceC = r;
                        break;
                    case 7:
                        sourceR = size - 1 - c;
                        sourceC = size - 1 - r;
                        break;
                }

                result[r * size + c] = raw[sourceR * size + sourceC];
            }
        }

        return new string(result);
    }

    private static string SwapRedGreen(string raw)
    {
        char[] chars = raw.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] == 'R') chars[i] = 'G';
            else if (chars[i] == 'G') chars[i] = 'R';
        }

        return new string(chars);
    }

    private static int HammingDistance(string a, string b)
    {
        int distance = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) distance++;
        }

        return distance;
    }

    private static UnityGameManager.BlockColor[,] ToColorGrid(List<UnityGameManager.BlockData> blocks, int size)
    {
        var grid = new UnityGameManager.BlockColor[size, size];
        foreach (UnityGameManager.BlockData block in blocks)
        {
            grid[block.row, block.col] = block.color;
        }

        return grid;
    }

    private static int DirectionIndex(UnityGameManager.Direction direction)
    {
        if (direction == UnityGameManager.Direction.Up) return 0;
        if (direction == UnityGameManager.Direction.Down) return 1;
        if (direction == UnityGameManager.Direction.Left) return 2;
        return 3;
    }

    private static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = rng.Next(i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private static char ColorChar(UnityGameManager.BlockColor color)
    {
        if (color == UnityGameManager.BlockColor.Red) return 'R';
        if (color == UnityGameManager.BlockColor.Green) return 'G';
        if (color == UnityGameManager.BlockColor.Blue) return 'B';
        if (color == UnityGameManager.BlockColor.Yellow) return 'Y';
        return 'N';
    }

    private static string LabelForColor(UnityGameManager.BlockColor color)
    {
        if (color == UnityGameManager.BlockColor.Red) return "RED";
        if (color == UnityGameManager.BlockColor.Green) return "GREEN";
        if (color == UnityGameManager.BlockColor.Blue) return "BLUE";
        if (color == UnityGameManager.BlockColor.Yellow) return "YELLOW";
        return "BLOCK";
    }

    private static UnityGameManager.LevelConfig BuildFallbackLevel(int boardIndex)
    {
        bool usesBlue = boardIndex >= FirstBlueBoardIndex;
        int blueMatchTarget = usesBlue ? GetBlueMatchTarget(boardIndex) : 0;
        int yellowMatchTarget = GetYellowMatchTarget(boardIndex);
        int size = GetBoardSize(boardIndex, usesBlue, yellowMatchTarget);
        int matchTarget = size;

        for (int attempt = 0; attempt < MaxGenerationAttempts * 2; attempt++)
        {
            var rng = new System.Random(SeedFor(boardIndex, MaxGenerationAttempts + 17 + attempt));

            if (!TryBuildRemovalPlan(size, rng, out int[] removalOrder, out UnityGameManager.Direction[] directions))
            {
                continue;
            }

            if (!TryBuildColorSequence(boardIndex, size, matchTarget, blueMatchTarget, yellowMatchTarget, rng, out List<UnityGameManager.BlockColor> colorSequence))
            {
                continue;
            }

            UnityGameManager.LevelConfig level = CreateLevelFromPlan(boardIndex, size, matchTarget, removalOrder, directions, colorSequence);
            if (!HasHealthyColorMix(level.blocks, size)) continue;
            if (!HasHealthyDirectionMix(level.blocks, size)) continue;
            if (HasFacingArrows(level.blocks, size)) continue;
            if (!ValidatePlannedSolution(level.blocks, size, removalOrder)) continue;
            if (!IsTraySequenceSolvable(colorSequence, matchTarget, blueMatchTarget, yellowMatchTarget)) continue;

            return level;
        }

        Debug.LogError($"Could not generate a valid fallback for Board {boardIndex + 1}. Returning an emergency board.");
        return BuildEmergencyLevel(boardIndex);
    }

    private static UnityGameManager.LevelConfig BuildEmergencyLevel(int boardIndex)
    {
        bool usesBlue = boardIndex >= FirstBlueBoardIndex;
        int blueMatchTarget = usesBlue ? GetBlueMatchTarget(boardIndex) : 0;
        int yellowMatchTarget = GetYellowMatchTarget(boardIndex);
        int size = GetBoardSize(boardIndex, usesBlue, yellowMatchTarget);
        int matchTarget = size;
        var colors = BuildColorBag(size, blueMatchTarget, yellowMatchTarget);

        var blocks = new List<UnityGameManager.BlockData>();
        for (int i = 0; i < size * size; i++)
        {
            UnityGameManager.BlockColor color = colors[i];
            blocks.Add(new UnityGameManager.BlockData
            {
                row = i / size,
                col = i % size,
                color = color,
                direction = i % 2 == 0 ? UnityGameManager.Direction.Left : UnityGameManager.Direction.Right,
                textLabel = LabelForColor(color)
            });
        }

        return new UnityGameManager.LevelConfig
        {
            id = boardIndex + 1,
            name = "Board " + (boardIndex + 1),
            boardSize = size,
            matchTarget = matchTarget,
            blocks = blocks
        };
    }
}
