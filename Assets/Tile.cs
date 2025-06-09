using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Tile : MonoBehaviour
{
    public int number;       // 0 = the empty tile
    public int row, col;     // logic position in the grid

    public TextMeshProUGUI numberText;
    Button btn;
    BoardManager board;

    public void Init(int num, int r, int c)
    {
        number = num;
        row = r;
        col = c;

        numberText.text = (number == 0 ? "" : number.ToString());

        board = FindObjectOfType<BoardManager>();
        btn = GetComponent<Button>();

        // only the non-empty tiles are clickable
        if (btn != null)
        {
            btn.interactable = (number != 0);
            if (number != 0)
                btn.onClick.AddListener(OnClick);
        }
    }

    void OnClick()
    {
        board.TryMove(this);
    }

    public void UpdatePosition(int newRow, int newCol)
    {
        row = newRow;
        col = newCol;
    }
}
