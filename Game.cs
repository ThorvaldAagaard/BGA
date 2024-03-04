using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using static BGA.Macros;
using Args = System.Timers.ElapsedEventArgs;
using Timer = System.Timers.Timer;

namespace BGA
{
    using Buttons = List<Button>;
    using Comparer = CardComparer;
    using Hand = List<Card>;
    using Orders = Dictionary<Trump, int[]>;

    public partial class Game : Form
    {
        private readonly Hand deck = new List<string>()
            { "S", "H", "D", "C" }.SelectMany(x =>
            new List<string>() { "A", "K", "Q", "J", "T",
                "9", "8", "7", "6", "5", "4", "3", "2" },
            (x, y) => Card.Parse(y + x)).ToList();

        private readonly Orders orders = new Orders
        {
            { Trump.Club, new int[4] { 0, 2, 3, 1 } },
            { Trump.Diamond, new int[4] { 1, 3, 2, 0 } },
            { Trump.Heart, new int[4] { 2, 3, 1, 0 } },
            { Trump.Spade, new int[4] { 3, 2, 0, 1 } },
            { Trump.None, new int[4] { 3, 2, 0, 1 } }
        };

        private int takenByNS = 0;
        private int takenByEW = 0;
        private float elapsed = 0f;
        private float maxThinkTime = 0.5f;
        private readonly PIMC PIMC = new PIMC();
        private readonly Buttons allCards = new Buttons();
        private readonly Comparer comparer = new Comparer();
        private readonly Font boldFont = new Font("Tahoma",
            8.5F, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font stndFont = new Font("Tahoma",
            8.5F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Hand played = new Hand();
        private readonly Timer timer = new Timer();
        private Details oldEastDetails;
        private Details oldWestDetails;
        private Details eastDetails;
        private Details westDetails;
        private Hand northHand = new Hand();
        private Hand southHand = new Hand();
        private Hand opposCards = new Hand();
        private Player leader = Player.West;
        private Player winningPlayer;
        private Card winningCard;

        public Game()
        {
            CheckForIllegalCrossThreadCalls = false;
            InitializeComponent();
            this.timer.Interval = 100.0;
            this.timer.Elapsed += OnEvaluation;
            this.timer.Enabled = false;
        }

        private void OnEvaluation(object source, Args e)
        {
            // return if no results to present
            if (this.PIMC.Output.Count == 0 ||
                !this.PIMC.Evaluating) return;

            // display results
            this.elapsed += 0.1f;
            this.LStats.Text = string.Format(
                "Declarer: {0} Ops: {1} Playouts: {2}",
                this.takenByNS, this.takenByEW, this.PIMC.Playouts);
            int minTricks = int.Parse(CRank.Text) + 6 - this.takenByNS;
            var panel = this.leader == 0 ? this.SNorth : this.SSouth;
            IEnumerable<string> legal = this.PIMC.LegalMoves;
            var labels = panel.Controls.OfType<Label>();
            float bestScore = -1f, bestTricks = -1f;
            Label bestMove = null;

            foreach (string card in legal)
            {
                // calculate win probability
                var set = this.PIMC.Output[card];
                float count = (float)set.Count;
                int makable = set.Count(t => t >= minTricks);
                float probability = (float)makable / count;
                if (float.IsNaN(probability)) probability = 0f;
                double tricks = count > 0 ? set.Average(t => (int)t) : 0;

                // draw probability in a label
                bool drawProb = probability > 0f && probability < 1f;
                Label label = labels.First(l => l.Name.Equals("L" + card));
                label.ForeColor = drawProb == true ? Color.MediumBlue :
                    probability == 0f ? Color.OrangeRed : Color.LimeGreen;
                label.Text = (drawProb == true ? probability : tricks)
                    .ToString("0.000").Replace(",", ".").Substring(0, 5);
                label.Font = this.stndFont;

                // find the best move
                if (bestScore.Equals(-1f) || probability > bestScore ||
                    bestScore.Equals(1f) && probability.Equals(1f) &&
                    tricks > bestTricks || bestScore.Equals(0f) &&
                    probability.Equals(0f) && tricks > bestTricks)
                {
                    bestMove = label;
                    bestScore = probability;
                    bestTricks = (float)tricks;
                }
            }

            // continue or stop evaluation
            if (bestMove == null) return;
            bestMove.Font = this.boldFont;
            if (this.elapsed < this.maxThinkTime) return;
            this.timer.Enabled = false;
            this.PIMC.EndEvaluate();
        }

        private void OnThinkingTimeChanged(object sender, EventArgs e)
        {
            float time = (float)(this.TBar.Value * 0.5);
            this.TTime.Text = time.ToString("0.0").Replace(',', '.');
            this.maxThinkTime = time;
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            // stop evaluation if we make the next move
            while (this.PIMC.Evaluating)
            {
                this.timer.Enabled = false;
                this.PIMC.EndEvaluate();
                Thread.Sleep(50);
            }

            // check each hand containing 13 cards
            if (this.TNHand.Text.Length != 16 ||
                this.TSHand.Text.Length != 16) return;

            this.Clear();
            this.GenerateCards();
            this.ResetDetails(0, ref this.eastDetails);
            this.ResetDetails(1, ref this.westDetails);
            this.oldEastDetails = this.eastDetails.Copy();
            this.oldWestDetails = this.westDetails.Copy();
            this.leader = this.CLeader.SelectedIndex
                == 0 ? Player.West : Player.East;
            this.takenByNS = this.takenByEW = 0;
            this.PlaceCards(this.PNorth, this.northHand);
            this.PlaceCards(this.PSouth, this.southHand);
            this.PlaceCards(this.leader.Equals(Player.West)
                ? this.PLeft : this.PRight, this.opposCards);
            this.LStats.Text = "Declarer: 0 Opps: 0 Playouts: 0";
        }

        private void OnThinkMoreClick(object sender, EventArgs e)
        {
            if (this.PIMC.Evaluating) return;
            int trump = this.CTrump.SelectedIndex;
            this.PIMC.BeginEvaluate((Trump)trump);
            this.elapsed = 0f;
            this.timer.Enabled = true;
        }

        private void Clear()
        {
            this.SNorth.Controls.OfType<Label>().ToList()
                .ForEach(label => label.Dispose());
            this.SSouth.Controls.OfType<Label>().ToList()
                .ForEach(label => label.Dispose());
            this.allCards.ForEach(b => b.Dispose());
            this.allCards.Clear();
            this.northHand.Clear();
            this.southHand.Clear();
            this.opposCards.Clear();
            this.played.Clear();
            this.ClearPool();
        }

        private void ClearPool()
        {
            this.BNPool.Text = "";
            this.BEPool.Text = "";
            this.BSPool.Text = "";
            this.BWPool.Text = "";
        }

        private void GenerateCards()
        {
            this.TNHand.Text = this.TNHand.Text.ToUpper();
            this.TSHand.Text = this.TSHand.Text.ToUpper();
            this.northHand = this.TNHand.Text.Parse();
            this.southHand = this.TSHand.Text.Parse();
            this.opposCards = this.deck.Except(
                this.northHand.Union(this.southHand,
                this.comparer), this.comparer).ToList();
        }

        private bool IsHigher(Card best, Card card)
        {
            Trump trump = (Trump)this.CTrump.SelectedIndex;
            bool t_play = trump != Trump.None;
            bool t_suit = best.Suit.Equals(card.Suit);
            bool t_best = best.Suit.Equals((Suit)trump);
            bool t_card = card.Suit.Equals((Suit)trump);
            if (t_play && t_best && !t_card) return false;
            if (t_play && !t_best && t_card) return true;
            if (t_play && !t_best && !t_suit) return false;
            if (!t_play && !t_suit) return false;
            return card.CompareTo(best) > 0;
        }

        internal IEnumerable<string> LegitMoves(Player player)
        {
            Hand cards = new List<Hand>() {
                this.northHand, this.opposCards,
                this.southHand, this.opposCards }[(int)player];
            var output = cards.Select(c => c.ToString());
            if (this.played.Count == 0) return output;
            var moves = cards.Where(c => this.played[0].Suit
                .Equals(c.Suit)).Select(c => c.ToString());
            return moves.Count() > 0 ? moves : output;
        }

        private void MakeMove(object sender, EventArgs e)
        {
            // stop evaluation if we make the next move
            while (this.PIMC.Evaluating)
            {
                this.timer.Enabled = false;
                this.PIMC.EndEvaluate();
                Thread.Sleep(50);
            }

            Card card = null;
            Button button = (Button)sender;
            Color color = button.Text.Contains('♣')
                ? Color.Green : button.Text.Contains('♦')
                ? Color.DarkOrange : button.Text.Contains('♥')
                ? Color.OrangeRed : Color.DarkBlue;

            // Move validator
            if ((this.leader == Player.North ||
                this.leader == Player.South) &&
                !this.LegitMoves(this.leader).Any(c =>
                button.Name.Equals("B" + c))) return;

            // Update pool
            if (this.played.Count == 0)
            {
                this.winningPlayer = this.leader;
                this.ClearPool();
            }
            if (this.leader == Player.West && this.PLeft.Contains(button))
            {
                card = this.opposCards.First(c =>
                    button.Name.Contains(c.ToString()));
                this.opposCards.Remove(card);
                this.BWPool.Text = button.Text;
                this.BWPool.ForeColor = color;
            }
            else if (this.leader == Player.North && this.PNorth.Contains(button))
            {
                card = this.northHand.First(c =>
                    button.Name.Contains(c.ToString()));
                this.northHand.Remove(card);
                this.BNPool.Text = button.Text;
                this.BNPool.ForeColor = color;
            }
            else if (this.leader == Player.East && this.PRight.Contains(button))
            {
                card = this.opposCards.First(c =>
                    button.Name.Contains(c.ToString()));
                this.opposCards.Remove(card);
                this.BEPool.Text = button.Text;
                this.BEPool.ForeColor = color;
            }
            else if (this.leader == Player.South && this.PSouth.Contains(button))
            {
                card = this.southHand.First(c =>
                    button.Name.Contains(c.ToString()));
                this.southHand.Remove(card);
                this.BSPool.Text = button.Text;
                this.BSPool.ForeColor = color;
            }
            
            if (card == null) return;
            if (this.leader == Player.North || this.leader == Player.South)
            {
                this.SNorth.Controls.OfType<Label>().ToList()
                    .ForEach(label => label.Dispose());
                this.SSouth.Controls.OfType<Label>().ToList()
                    .ForEach(label => label.Dispose());
            }
            else if (this.leader == Player.West || this.leader == Player.East)
            {
                int hcp = card.HCP();
                Details newDetails = this.leader == Player.East
                    ? this.eastDetails : this.westDetails;
                int prevMin = newDetails[card.Suit, 0];
                int prevMax = newDetails[card.Suit, 1];
                newDetails[card.Suit, 0] = Math.Max(0, prevMin - 1);
                newDetails[card.Suit, 1] = Math.Max(0, prevMax - 1);
                newDetails.MinHCP = Math.Max(0, newDetails.MinHCP - hcp);
                newDetails.MaxHCP = Math.Max(0, newDetails.MaxHCP - hcp);
                if (this.played.Count > 0 && card.Suit != this.played[0].Suit)
                {
                    Details oldDetails = this.leader == Player.East
                        ? this.oldEastDetails : this.oldWestDetails;
                    newDetails[this.played[0].Suit, 0] = 0;
                    newDetails[this.played[0].Suit, 1] = 0;
                    oldDetails[this.played[0].Suit, 0] = 0;
                    oldDetails[this.played[0].Suit, 1] = 0;
                }
            }

            // Update state
            this.allCards.Remove(button);
            this.played.Add(card);

            Card best = this.winningCard;
            Card last = this.played.Last();
            bool firstMove = this.played.Count == 1;
            if (firstMove) this.winningCard = last;
            button.Dispose();

            // Decide which card is winning and pick new leader
            if (!firstMove && this.IsHigher(best, card))
            {
                this.winningPlayer = this.leader;
                this.winningCard = card;
            }
            if (this.played.Count >= 4)
            {
                this.oldEastDetails = this.eastDetails.Copy();
                this.oldWestDetails = this.westDetails.Copy();
                // MessageBox.Show(this.oldWestDetails.ToString());
                // MessageBox.Show(this.oldEastDetails.ToString());
                this.leader = this.winningPlayer;
                bool ns = (int)this.leader % 2 == 0;
                if (ns) this.takenByNS++;
                else this.takenByEW++;
                this.played.Clear();
            }
            else
            {
                this.leader = this.leader.Next();
            }

            // Update statistics
            this.LStats.Text = string.Format("Declarer: {0} Opps: {1}"
                + " Playouts: 0", this.takenByNS, this.takenByEW);
            if (this.takenByNS + this.takenByEW >= 13) return;

            // Display opponents cards
            if (this.leader == Player.West &&
                this.PLeft.Controls.Count == 0)
                this.MoveCards(this.PRight, this.PLeft);
            if (this.leader == Player.East &&
                this.PRight.Controls.Count == 0)
                this.MoveCards(this.PLeft, this.PRight);
            if (this.leader == Player.West ||
                this.leader == Player.East) return;

            // AI evaluation ...

            // Prepare labels for each legal move
            if (this.takenByNS + this.takenByEW >= 12) return;
            FlowLayoutPanel panel = this.leader ==
                Player.North ? this.PNorth : this.PSouth;
            Panel scorePanel = this.leader ==
                Player.North ? this.SNorth : this.SSouth;
            var buttons = panel.Controls.OfType<Button>();
            var legal = this.LegitMoves(this.leader);
            foreach (string move in legal)
            {
                button = buttons.First(b =>
                    b.Name.Equals("B" + move));
                Label label = new Label();
                scorePanel.Controls.Add(label);
                label.AutoSize = true; label.Font = this.stndFont;
                label.Location = new Point(button.Location.X, 1);
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Name = "L" + move;
            }

            // Run algorithm
            this.PIMC.Clear();
            if (this.northHand.Count == 0 ||
                this.southHand.Count == 0) return;
            this.PIMC.SetupEvaluation(new Hand[2] {
                this.northHand, this.southHand },
                this.opposCards, this.played, legal,
                new Details[2] { this.oldEastDetails,
                this.oldWestDetails }, this.leader);
            int trump = this.CTrump.SelectedIndex;
            this.PIMC.BeginEvaluate((Trump)trump);
            this.elapsed = 0f;
            this.timer.Enabled = true;
        }

        private void MoveCards(FlowLayoutPanel from, FlowLayoutPanel to)
        {
            Buttons cards = new Buttons();
            cards.AddRange(from.Controls.OfType<Button>());
            foreach (Button button in cards) to.Controls.Add(button);
            from.Controls.Clear();
        }

        private void PlaceCards(FlowLayoutPanel panel, Hand cards)
        {
            var order = this.orders[(Trump)this.CTrump.SelectedIndex];
            Hand hand = new Hand(); for (int i = 0; i < 4; i++)
                hand.AddRange(cards.Where(c => (int)c.Suit == order[i]));
            foreach (Card card in hand) this.CreateButton(panel, card);
        }

        private void ResetDetails(int side, ref Details details)
        {
            Regex rgx = new Regex(@"\d+");
            int minClubs = int.Parse(side == 1 ?
                rgx.Match(this.CLeftClubsMin.Text).Value :
                rgx.Match(this.CRightClubsMin.Text).Value);
            int maxClubs = int.Parse(side == 1 ?
                rgx.Match(this.CLeftClubsMax.Text).Value :
                rgx.Match(this.CRightClubsMax.Text).Value);
            int minDiamonds = int.Parse(side == 1 ?
                rgx.Match(this.CLeftDiamondsMin.Text).Value :
                rgx.Match(this.CRightDiamondsMin.Text).Value);
            int maxDiamonds = int.Parse(side == 1 ?
                rgx.Match(this.CLeftDiamondsMax.Text).Value :
                rgx.Match(this.CRightDiamondsMax.Text).Value);
            int minHearts = int.Parse(side == 1 ?
                rgx.Match(this.CLeftHeartsMin.Text).Value :
                rgx.Match(this.CRightHeartsMin.Text).Value);
            int maxHearts = int.Parse(side == 1 ?
                rgx.Match(this.CLeftHeartsMax.Text).Value :
                rgx.Match(this.CRightHeartsMax.Text).Value);
            int minSpades = int.Parse(side == 1 ?
                rgx.Match(this.CLeftSpadesMin.Text).Value :
                rgx.Match(this.CRightSpadesMin.Text).Value);
            int maxSpades = int.Parse(side == 1 ?
                rgx.Match(this.CLeftSpadesMax.Text).Value :
                rgx.Match(this.CRightSpadesMax.Text).Value);
            int minHcp = int.Parse(side == 1 ?
                rgx.Match(this.TLeftHCPMin.Text).Value :
                rgx.Match(this.TRightHCPMin.Text).Value);
            int maxHcp = int.Parse(side == 1 ?
                rgx.Match(this.TLeftHCPMax.Text).Value :
                rgx.Match(this.TRightHCPMax.Text).Value);
            details = new Details(minClubs, maxClubs,
                minDiamonds, maxDiamonds, minHearts, maxHearts,
                minSpades, maxSpades, minHcp, maxHcp);
        }

        private void CreateButton(FlowLayoutPanel panel, Card card)
        {
            char suit = "♣♦♥♠"[(int)card.Suit];
            Color color = suit == '♣' ? Color.Green :
                suit == '♦' ? Color.DarkOrange : suit ==
                '♥' ? Color.OrangeRed : Color.DarkBlue;
            Font font = new Font("Tahoma", 12F,
                FontStyle.Bold, GraphicsUnit.Point);
            Button button = new Button();
            button.BackColor = Color.White;
            button.Click += MakeMove;
            button.Font = font;
            button.ForeColor = color;
            button.Margin = new Padding(2);
            button.Name = "B" + card.ToString();
            button.Size = new Size(40, 40);
            button.Text = $"{card.Rank}{suit}";
            panel.Controls.Add(button);
            this.allCards.Add(button);
        }
    }
}
