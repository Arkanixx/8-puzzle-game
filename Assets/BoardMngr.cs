using UnityEngine;
using UnityEngine.UI;    // ← make sure you have this!
using System.Linq;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;           // for EditorUtility.OpenFilePanel
#endif
using System.IO;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;
using System.Diagnostics;


public class BoardManager : MonoBehaviour
{
    [Header("Tile Puzzle Setup")]
    public GameObject tilePrefab;
    public Transform boardParent;               // your 3×3 GridLayoutGroup parent
    private Tile[,] grid = new Tile[3, 3];

    [Header("Picture Puzzle")]
    public Button loadImageButton;              // hook this to your “Load Image” button
    public Button viewOriginalButton;           // hook this to your “View Original” button
    public GameObject originalImagePanel;       // the panel (initially inactive) that holds the Image
    public Image originalImageDisplay;          // the Image component that shows the full sprite

    private Texture2D lastLoadedTexture;        // remember the raw texture
    private Sprite fullImageSprite;          // the sprite we’ll assign for “View Original”

    [Header("Win UI")]
    public GameObject winPanel;
    public TextMeshProUGUI winMessageText;

    [Header("Move counter")]
    public TextMeshProUGUI moveCounterText;
    private int moveCount = 0;

    [Header("Auto-Solve")]
    public Button autoSolveButton;
    public float autoSolveDelay = 0.5f;

    class Node
    {
        public int[] state;      // length 9, row-major: 0..8
        public int g;          // cost so far
        public int h;          // heuristic
        public Node parent;     // to reconstruct path
        public int movedTile;  // the tile number we moved to get here (for replay)
        public int f => g + h;
    }

    List<int> solutionMoves = null;

    int Heuristic(int[] state)
    {
        int dist = 0;
        for (int i = 0; i < 9; i++)
        {
            int val = state[i];
            if (val == 0) continue;
            int targetIndex = val - 1;
            int r1 = i / 3, c1 = i % 3;
            int r2 = targetIndex / 3, c2 = targetIndex % 3;
            dist += Mathf.Abs(r1 - r2) + Mathf.Abs(c1 - c2);
        }
        return dist;
    }

    [Header("BFS-Solve")]
    public Button bfsSolveButton;
    public float bfsSolveDelay = 0.5f;

    // you can reuse the same Node class (without h):
    class BFNode
    {
        public int[] state;      // 9-length, row-major
        public BFNode parent;
        public int movedTile;  // which tile moved into zero
    }


    int[,] initial = {
        { 1, 2, 3 },
        { 4, 5, 6 },
        { 7, 8, 0 }
    };

    void Start()
    { 
        SpawnTiles();
        // Wire up any button callbacks here if you haven’t done so in the Inspector:
        if (viewOriginalButton != null)
            viewOriginalButton.onClick.AddListener(ShowOriginal);
        // If you want the panel to close when clicking the image itself:
        if (originalImageDisplay != null)
        {
            Button imgBtn = originalImageDisplay.GetComponent<Button>();
            if (imgBtn != null)
                imgBtn.onClick.AddListener(HideOriginal);
        }
        moveCount = 0;
        UpdateMoveCounter();
    }

    void SpawnTiles()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                var go = Instantiate(tilePrefab, boardParent);
                var t = go.GetComponent<Tile>();
                t.Init(initial[r, c], r, c);
                grid[r, c] = t;
            }
    }

    public void TryMove(Tile t)
    {
        var empty = GetEmptyTile();
        if (empty != null && IsAdjacent(t, empty))
            Swap(t, empty);
            CheckWin();

        moveCount++;
        UpdateMoveCounter();
    }

    Tile GetEmptyTile()
    {
        // Linear search for the one whose number == 0
        foreach (var t in grid)
            if (t.number == 0) return t;
        return null;
    }
    List<Tile> GetAdjacentTiles(Tile t)
    {
        var neighbors = new List<Tile>();

        int r = t.row, c = t.col;
        if (r > 0) neighbors.Add(grid[r - 1, c]);
        if (r < 2) neighbors.Add(grid[r + 1, c]);
        if (c > 0) neighbors.Add(grid[r, c - 1]);
        if (c < 2) neighbors.Add(grid[r, c + 1]);

        return neighbors;
    }

    bool IsAdjacent(Tile a, Tile b)
    {
        // orthogonally adjacent?
        return (Mathf.Abs(a.row - b.row) == 1 && a.col == b.col)
            || (Mathf.Abs(a.col - b.col) == 1 && a.row == b.row);
    }

    void Swap(Tile a, Tile b)
    {
        // 1) swap in the grid array
        grid[a.row, a.col] = b;
        grid[b.row, b.col] = a;

        // 2) swap their row/col fields
        int ar = a.row, ac = a.col;
        a.UpdatePosition(b.row, b.col);
        b.UpdatePosition(ar, ac);

        // 3) swap their positions under the GridLayoutGroup
        int idxA = a.transform.GetSiblingIndex();
        int idxB = b.transform.GetSiblingIndex();
        a.transform.SetSiblingIndex(idxB);
        b.transform.SetSiblingIndex(idxA);
    }
    void ShuffleBoard(int moves)
    {
        var rand = new System.Random();
        for (int i = 0; i < moves; i++)
        {
            var empty = GetEmptyTile();
            var adj = GetAdjacentTiles(empty);
            var choice = adj[rand.Next(adj.Count)];
            Swap(choice, empty);
        }
    }
    public void Shuffle()
    {
        ShuffleBoard(100);
    }

    public void LoadImage()
    {
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel(
            "Select a picture",
            "",
            "png,jpg,jpeg"
        );
        if (string.IsNullOrEmpty(path)) return;

        byte[] fileBytes = File.ReadAllBytes(path);
        Texture2D tex = new Texture2D(2, 2);
        if (tex.LoadImage(fileBytes))
        {
            lastLoadedTexture = tex;
            CreateFullSprite(tex);      // create a sprite of the entire image
            SliceAndApply(tex);         // slice into the 3×3 tiles
        }
        Shuffle();
#else
        // In a standalone build, use a plugin like
        // https://github.com/gkngkc/UnityStandaloneFileBrowser
#endif
    }

    /// <summary>
    /// Make a Sprite out of the full Texture2D and store it.
    /// We'll use this when the user hits “View Original.”
    /// </summary>
    void CreateFullSprite(Texture2D tex)
    {
        fullImageSprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100   // pixels per unit (adjust if needed)
        );

        // Assign it immediately to the hidden Image component:
        if (originalImageDisplay != null)
            originalImageDisplay.sprite = fullImageSprite;
    }

    /// <summary>
    /// Cut the loaded texture into 3×3 slices and assign each piece
    /// to the matching Tile (by number). 0 = blank, so we disable its Image.
    /// </summary>
    void SliceAndApply(Texture2D image)
    {
        int rows = 3, cols = 3;
        int tileW = image.width / cols;
        int tileH = image.height / rows;

        // Create a 2D array of Sprites
        Sprite[,] sprites = new Sprite[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Rect rect = new Rect(
                    c * tileW,
                    (rows - 1 - r) * tileH, // flip Y because Tex origin is bottom-left
                    tileW,
                    tileH
                );

                sprites[r, c] = Sprite.Create(
                    image,
                    rect,
                    new Vector2(0.5f, 0.5f),
                    100
                );
            }
        }

        // Assign each tile’s Image component accordingly
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Tile t = grid[r, c];
                Image img = t.GetComponent<Image>();

                if (t.number == 0)
                {
                    // Hide the empty tile
                    img.enabled = false;
                }
                else
                {
                    int idx = t.number - 1;
                    int sr = idx / cols;
                    int sc = idx % cols;
                    img.sprite = sprites[sr, sc];
                    img.enabled = true;
                }
            }
        }
    }
    public void ShowOriginal()
    {
        if (fullImageSprite == null) return;
        if (originalImagePanel != null)
            originalImagePanel.SetActive(true);
    }

    /// <summary>
    /// Called when the user clicks the full‐screen image (or a Close button).
    /// </summary>
    public void HideOriginal()
    {
        if (originalImagePanel != null)
            originalImagePanel.SetActive(false);
    }

    /// <summary>
    /// Scans the grid; returns true if tiles are in solved order:
    /// 1,2,3
    /// 4,5,6
    /// 7,8,0
    /// </summary>
    bool IsSolved()
    {
        // expected number for each r,c is r*3 + c + 1, except final slot is 0
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                int shouldBe = (r * 3 + c + 1) % 9;
                if (grid[r, c].number != shouldBe)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Call this whenever you want to see if the puzzle is done.
    /// </summary>
    void CheckWin()
    {
        if (IsSolved())
        {
            if (winMessageText != null)
                winMessageText.text = $"You solved it in {moveCount} moves! ";

            if (winPanel != null)
                winPanel.SetActive(true);
        }
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void UpdateMoveCounter()
    {
        moveCounterText.text = $"Moves: {moveCount}";
    }

    // Given a 1-D state array, return all legal neighbor nodes
    IEnumerable<Node> GetNeighbors(Node n)
    {
        int zeroPos = System.Array.IndexOf(n.state, 0);
        int zr = zeroPos / 3, zc = zeroPos % 3;
        int[] dr = { -1, +1, 0, 0 };
        int[] dc = { 0, 0, -1, +1 };
        for (int i = 0; i < 4; i++)
        {
            int nr = zr + dr[i], nc = zc + dc[i];
            if (nr < 0 || nr > 2 || nc < 0 || nc > 2) continue;
            int swapPos = nr * 3 + nc;
            // create new state
            int[] newState = (int[])n.state.Clone();
            // swap 0 and the neighbor
            newState[zeroPos] = newState[swapPos];
            newState[swapPos] = 0;
            var child = new Node
            {
                state = newState,
                g = n.g + 1,
                h = 0,             // fill below
                parent = n,
                movedTile = newState[zeroPos]  // that moved into the blank
            };
            child.h = Heuristic(child.state);
            yield return child;
        }
    }

    List<int> SolvePuzzleAStar(int[] startState)
    {
        var openSet = new List<Node>();
        var closedSet = new HashSet<string>();
        var start = new Node
        {
            state = startState,
            g = 0,
            h = Heuristic(startState),
            parent = null,
            movedTile = -1
        };
        openSet.Add(start);

        while (openSet.Count > 0)
        {
            // pick node with lowest f
            openSet.Sort((a, b) => a.f.CompareTo(b.f));
            var current = openSet[0];
            openSet.RemoveAt(0);

            string key = string.Join(",", current.state);
            if (closedSet.Contains(key))
                continue;

            // goal?
            if (current.h == 0)
            {
                // reconstruct move list
                var moves = new List<int>();
                var n = current;
                while (n.parent != null)
                {
                    moves.Add(n.movedTile);
                    n = n.parent;
                }
                moves.Reverse();
                return moves;
            }

            closedSet.Add(key);

            foreach (var neighbor in GetNeighbors(current))
            {
                string nk = string.Join(",", neighbor.state);
                if (closedSet.Contains(nk)) continue;
                openSet.Add(neighbor);
            }
        }

        return null; // unsolvable?
    }

    public void OnAutoSolvePressedAStart()
    {
        // read current board into a flat array
        int[] flat = new int[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                flat[r * 3 + c] = grid[r, c].number;

        solutionMoves = SolvePuzzleAStar(flat);
        if (solutionMoves != null && solutionMoves.Count > 0)
        {
            StartCoroutine(AnimateSolution());
        }
        else
        {
            UnityEngine.Debug.LogWarning("No solution found!");
        }
    }

    // step through the solution
    IEnumerator AnimateSolution()
    {
        // disable player input while auto-solving
        autoSolveButton.interactable = false;
        Button[] allBtns = boardParent.GetComponentsInChildren<Button>();
        foreach (var b in allBtns) b.interactable = false;

        foreach (int tileNum in solutionMoves)
        {
            // find the Tile instance with that number
            Tile t = grid.Cast<Tile>().First(x => x.number == tileNum);
            TryMove(t);
            yield return new WaitForSeconds(autoSolveDelay);
        }

        // re-enable input
        autoSolveButton.interactable = true;
        foreach (var b in allBtns) b.interactable = true;
    }

    List<int> SolvePuzzleBFS(int[] startState)
    {
        var visited = new HashSet<string>();
        var q = new Queue<BFNode>();

        var root = new BFNode { state = startState, parent = null, movedTile = -1 };
        q.Enqueue(root);
        visited.Add(StateKey(startState));

        while (q.Count > 0)
        {
            var n = q.Dequeue();

            // goal test: blank in last spot and everything else in order
            if (IsGoal(n.state))
            {
                // reconstruct moves
                var moves = new List<int>();
                for (var cur = n; cur.parent != null; cur = cur.parent)
                    moves.Add(cur.movedTile);
                moves.Reverse();
                return moves;
            }

            // expand neighbors
            foreach (var child in GetBFSNeighbors(n))
            {
                string key = StateKey(child.state);
                if (visited.Add(key))
                    q.Enqueue(child);
            }
        }

        return null;
    }

    IEnumerable<BFNode> GetBFSNeighbors(BFNode n)
    {
        int zeroPos = System.Array.IndexOf(n.state, 0);
        int zr = zeroPos / 3, zc = zeroPos % 3;
        int[] dr = { -1, +1, 0, 0 };
        int[] dc = { 0, 0, -1, +1 };

        for (int i = 0; i < 4; i++)
        {
            int nr = zr + dr[i], nc = zc + dc[i];
            if (nr < 0 || nr > 2 || nc < 0 || nc > 2) continue;

            int swapPos = nr * 3 + nc;
            int[] newState = (int[])n.state.Clone();
            // swap
            newState[zeroPos] = newState[swapPos];
            newState[swapPos] = 0;

            yield return new BFNode
            {
                state = newState,
                parent = n,
                movedTile = newState[zeroPos]
            };
        }
    }

    bool IsGoal(int[] state)
    {
        for (int i = 0; i < 8; i++)
            if (state[i] != i + 1) return false;
        return state[8] == 0;
    }

    string StateKey(int[] state) => string.Join(",", state);

    public void OnBFSSolvePressed()
    {
        // read current grid into flat array
        int[] flat = new int[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                flat[r * 3 + c] = grid[r, c].number;

        // time the BFS
        var sw = Stopwatch.StartNew();
        var bfsMoves = SolvePuzzleBFS(flat);
        sw.Stop();
        UnityEngine.Debug.Log($"BFS found {bfsMoves?.Count ?? 0} moves in {sw.ElapsedMilliseconds} ms");

        if (bfsMoves != null)
        {
            StartCoroutine(AnimateSolution(bfsMoves, bfsSolveDelay, bfsSolveButton));
        }
        else
        {
            UnityEngine.Debug.LogWarning("BFS: No solution found!");
        }
    }

    // reuse your AnimateSolution but parameterize the move list and button
    IEnumerator AnimateSolution(List<int> moves, float delay, Button disableButton)
    {
        // disable both solve buttons and tile input
        autoSolveButton.interactable = false;
        bfsSolveButton.interactable = false;
        foreach (var b in boardParent.GetComponentsInChildren<Button>())
            b.interactable = false;

        foreach (int tileNum in moves)
        {
            var tile = grid.Cast<Tile>().First(x => x.number == tileNum);
            TryMove(tile);
            yield return new WaitForSeconds(delay);
        }

        // re-enable
        autoSolveButton.interactable = true;
        bfsSolveButton.interactable = true;
        foreach (var b in boardParent.GetComponentsInChildren<Button>())
            b.interactable = true;
    }
}
