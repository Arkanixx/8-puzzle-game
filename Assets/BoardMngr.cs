using UnityEngine;
using UnityEngine.UI;    // ← make sure you have this!
using System.Linq;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;           // for EditorUtility.OpenFilePanel
#endif
using System.IO;
using UnityEngine.SceneManagement;

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
            // show your panel
            if (winPanel != null)
                winPanel.SetActive(true);
        }
    }

    public void RestartGame()
    {
        // hide the win panel
        winPanel.SetActive(false);

        // put tiles back into solved order in both grid[] and UI
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                Tile t = grid[r, c];
                // the instance whose number == (r*3 + c + 1)%9 is already in place,
                // so you could either re-SpawnTiles() or swap until solved.
                // Simplest: reload scene. Otherwise you'd need a ResetBoard() helper.
            }

        // then shuffle
        ShuffleBoard(100);
    }
}
