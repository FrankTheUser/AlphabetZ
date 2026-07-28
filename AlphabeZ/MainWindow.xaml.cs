using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AgendaAppleStyle
{
    public class UserData
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool AutoLogin { get; set; }
    }

    public partial class MainWindow : Window
    {
        private string configPath = "user_config.json";
        private string phrasesPath = "frasi_salvate.json";

        private UserData currentUser;
        private List<string> tutteLeFrasi = new List<string>();
        private string letteraCorrente = "•";
        private bool isRegistrationMode = false;

        private DispatcherTimer timerClock;
        private string ultimaOra = "";
        private string ultimiMinuti = "";

        private static readonly HttpClient httpClient = new HttpClient();

        public MainWindow()
        {
            InitializeComponent();
            InizializzaAlfabeto();
            AvviaLavaLampConBolle();
            InizializzaFlipClock();

            TxtNuovaFrase.GotFocus += (s, e) => { if (TxtNuovaFrase.Text == "Scrivi una nuova parola e premi Invio...") TxtNuovaFrase.Text = ""; };
            TxtNuovaFrase.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(TxtNuovaFrase.Text)) TxtNuovaFrase.Text = "Scrivi una nuova parola e premi Invio..."; };

            ControllaAutoLogin();
        }

        // --- GESTIONE NUVOLETTA  ---
        private async void TextBlock_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (sender is TextBlock tb)
            {
                string fraseCompleta = tb.Text;

                
                ToolTip tt = tb.ToolTip as ToolTip;
                if (tt == null)
                {
                    tt = new ToolTip();
                    tb.ToolTip = tt;
                }

                
                tt.Content = "🔍 Ricerca nel dizionario...";

                
                string definizione = await OttieniDefinizioneDinamicaAsync(fraseCompleta);

                
                tt.Content = definizione;
            }
        }

        private async Task<string> OttieniDefinizioneDinamicaAsync(string testo)
        {
            if (string.IsNullOrWhiteSpace(testo))
                return "Nessun testo presente.";

            
            string[] parole = testo.Split(new[] { ' ', ',', '.', ';', ':', '!', '?', '"', '\'', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            if (parole.Length == 0) return $"Frase: \"{testo}\"";

            string parolaChiave = parole[0].Trim();

            
            string definizione = await CercaDefinizioneMediaWikiAsync("it.wiktionary.org", parolaChiave);

            
            if (string.IsNullOrEmpty(definizione))
            {
                definizione = await CercaDefinizioneMediaWikiAsync("it.wikipedia.org", parolaChiave);
            }

            
            if (!string.IsNullOrEmpty(definizione))
            {
                string infoFrase = parole.Length > 1 ? $"(Parola nel Notebook: \"{testo}\")\n\n" : "";
                return $"📖 {parolaChiave.ToUpper()}\n\n{infoFrase}{definizione}";
            }

            
            int conteggioParole = parole.Length;
            return $"📝 \"{testo}\"\n\n(Composta da {conteggioParole} {(conteggioParole == 1 ? "parola" : "parole")}\nDefinizione non trovata nel dizionario)";
        }

        private async Task<string> CercaDefinizioneMediaWikiAsync(string dominio, string parola)
        {
            try
            {
                parola = parola.Trim();
                if (string.IsNullOrEmpty(parola)) return null;

                
                string parolaLower = parola.ToLower();
                string parolaCap = char.ToUpper(parola[0]) + (parola.Length > 1 ? parola.Substring(1).ToLower() : "");

                string[] varianti = new[] { parolaLower, parolaCap, parola };

                foreach (var variante in varianti.Distinct())
                {
                    // Query 
                    string url = $"https://{dominio}/w/api.php?action=query&prop=extracts&exintro=1&explaintext=1&redirects=1&format=json&titles={Uri.EscapeDataString(variante)}";

                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        
                        request.Headers.UserAgent.Clear();
                        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AlphabetZApp/1.0 (contact@example.com)");

                        HttpResponseMessage response = await httpClient.SendAsync(request);
                        if (!response.IsSuccessStatusCode) continue;

                        string json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("query", out var query) &&
                                query.TryGetProperty("pages", out var pages))
                            {
                                foreach (var page in pages.EnumerateObject())
                                {
                                    // Se la pagina ha un ID diverso da -1, la voce esiste
                                    if (page.Name != "-1" && page.Value.TryGetProperty("extract", out JsonElement extractProp))
                                    {
                                        string testoEstratto = extractProp.GetString();
                                        if (!string.IsNullOrWhiteSpace(testoEstratto))
                                        {
                                            testoEstratto = testoEstratto.Trim();

                                            if (testoEstratto.Length > 280)
                                            {
                                                testoEstratto = testoEstratto.Substring(0, 280) + "...";
                                            }

                                            return testoEstratto;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                
            }

            return null;
        }

        // --- FLIP CLOCK & DATA ---
        private void InizializzaFlipClock()
        {
            timerClock = new DispatcherTimer();
            timerClock.Interval = TimeSpan.FromSeconds(1);
            timerClock.Tick += TimerClock_Tick;
            timerClock.Start();

            AggiornaOrologioEData(false);
        }

        private void TimerClock_Tick(object sender, EventArgs e) => AggiornaOrologioEData(true);

        private void AggiornaOrologioEData(bool anima)
        {
            DateTime oraAttuale = DateTime.Now;

            CultureInfo ci = new CultureInfo("it-IT");
            TxtDataDettaglio.Text = oraAttuale.ToString("dddd, d MMMM", ci);

            TxtDots.Opacity = oraAttuale.Second % 2 == 0 ? 1.0 : 0.3;

            string strOre = oraAttuale.ToString("HH");
            string strMinuti = oraAttuale.ToString("mm");

            if (strOre != ultimaOra)
            {
                if (anima) AnimaFlipCard(GridOre, TxtOre, strOre);
                else TxtOre.Text = strOre;
                ultimaOra = strOre;
            }

            if (strMinuti != ultimiMinuti)
            {
                if (anima) AnimaFlipCard(GridMinuti, TxtMinuti, strMinuti);
                else TxtMinuti.Text = strMinuti;
                ultimiMinuti = strMinuti;
            }
        }

        private void AnimaFlipCard(Grid containerGrid, TextBlock txtBlock, string nuovoTesto)
        {
            ScaleTransform scaleTransform = new ScaleTransform(1, 1, containerGrid.ActualWidth / 2, containerGrid.ActualHeight / 2);
            containerGrid.RenderTransform = scaleTransform;

            DoubleAnimation anim1 = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            DoubleAnimation anim2 = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));

            anim1.Completed += (s, e) =>
            {
                txtBlock.Text = nuovoTesto;
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim2);
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim1);
        }

        // --- LAVA LAMP ---
        private void AvviaLavaLampConBolle()
        {
            PointAnimation moveStart = new PointAnimation(new Point(0, 0), new Point(1, 0), TimeSpan.FromSeconds(6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            PointAnimation moveEnd = new PointAnimation(new Point(1, 1), new Point(0, 1), TimeSpan.FromSeconds(8)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            LavaGradient.BeginAnimation(LinearGradientBrush.StartPointProperty, moveStart);
            LavaGradient.BeginAnimation(LinearGradientBrush.EndPointProperty, moveEnd);

            AnimaBolla(Bubble1, 50, 500, 7);
            AnimaBolla(Bubble2, -20, 480, 10);
            AnimaBolla(Bubble3, 100, 520, 6);
            AnimaBolla(Bubble4, -50, 450, 9);
        }

        private void AnimaBolla(UIElement bolla, double startY, double endY, double secondi)
        {
            DoubleAnimation anim = new DoubleAnimation(startY, endY, TimeSpan.FromSeconds(secondi))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase() { EasingMode = EasingMode.EaseInOut }
            };
            bolla.BeginAnimation(Canvas.TopProperty, anim);
        }

        // --- LOGIN & AUTO-LOGIN ---
        private void ControllaAutoLogin()
        {
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                currentUser = JsonSerializer.Deserialize<UserData>(json);

                if (currentUser != null && currentUser.AutoLogin)
                {
                    EntraNellApp();
                    return;
                }
            }
            ViewLogin.Visibility = Visibility.Visible;
            ViewApp.Visibility = Visibility.Collapsed;
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string u = TxtUser.Text.Trim();
            string p = TxtPass.Password.Trim();

            if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p))
            {
                MessageBox.Show("Inserisci il Nome Utente e la Password!", "AlphabetZ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (isRegistrationMode)
            {
                currentUser = new UserData { Username = u, Password = p, AutoLogin = ChkAutoLogin.IsChecked == true };
                SalvaConfig();
                MessageBox.Show("Registrazione completata su AlphabetZ!", "Benvenuto", MessageBoxButton.OK, MessageBoxImage.Information);
                EntraNellApp();
            }
            else
            {
                if (currentUser == null && File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    currentUser = JsonSerializer.Deserialize<UserData>(json);
                }

                if (currentUser != null && currentUser.Username == u && currentUser.Password == p)
                {
                    currentUser.AutoLogin = ChkAutoLogin.IsChecked == true;
                    SalvaConfig();
                    EntraNellApp();
                }
                else
                {
                    MessageBox.Show("Nome utente o password non corretti!", "Errore AlphabetZ", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnToggleAuthMode_Click(object sender, RoutedEventArgs e)
        {
            isRegistrationMode = !isRegistrationMode;
            if (isRegistrationMode)
            {
                TxtLoginTitolo.Text = "Crea un nuovo account";
                BtnSubmit.Content = "REGISTRATI";
                BtnToggleAuthMode.Content = "Hai già un account? Accedi";
            }
            else
            {
                TxtLoginTitolo.Text = "Accedi al tuo Notebook A-Z";
                BtnSubmit.Content = "ENTRA";
                BtnToggleAuthMode.Content = "Non hai un account? Registrati";
            }
        }

        private void EntraNellApp()
        {
            ViewLogin.Visibility = Visibility.Collapsed;
            ViewApp.Visibility = Visibility.Visible;

            TxtWelcome.Text = $"Ciao, {currentUser.Username}!";
            ChkAutoLoginSettings.IsChecked = currentUser.AutoLogin;

            CaricaFrasiDaFile();
        }

        private void SalvaConfig()
        {
            string json = JsonSerializer.Serialize(currentUser);
            File.WriteAllText(configPath, json);
        }

        // --- OPZIONI & LOGOUT ---
        private void BtnOpzioni_Click(object sender, RoutedEventArgs e) => PanelOpzioni.Visibility = Visibility.Visible;
        private void BtnChiudiOpzioni_Click(object sender, RoutedEventArgs e) => PanelOpzioni.Visibility = Visibility.Collapsed;

        private void ChkAutoLoginSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (currentUser != null)
            {
                currentUser.AutoLogin = ChkAutoLoginSettings.IsChecked == true;
                SalvaConfig();
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            if (currentUser != null)
            {
                currentUser.AutoLogin = false;
                SalvaConfig();
            }
            PanelOpzioni.Visibility = Visibility.Collapsed;
            ViewApp.Visibility = Visibility.Collapsed;
            ViewLogin.Visibility = Visibility.Visible;
            TxtUser.Text = "";
            TxtPass.Password = "";
        }

        private void CambiaColore_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            string mood = btn.Tag.ToString();
            Color c1, c2, c3;

            switch (mood)
            {
                case "Rosa": c1 = (Color)ColorConverter.ConvertFromString("#FF2D55"); c2 = (Color)ColorConverter.ConvertFromString("#FF9500"); c3 = (Color)ColorConverter.ConvertFromString("#AF52DE"); break;
                case "Smeraldo": c1 = (Color)ColorConverter.ConvertFromString("#34C759"); c2 = (Color)ColorConverter.ConvertFromString("#00C7BE"); c3 = (Color)ColorConverter.ConvertFromString("#30B0C7"); break;
                case "Viola": c1 = (Color)ColorConverter.ConvertFromString("#AF52DE"); c2 = (Color)ColorConverter.ConvertFromString("#5856D6"); c3 = (Color)ColorConverter.ConvertFromString("#FF2D55"); break;
                default: c1 = (Color)ColorConverter.ConvertFromString("#007AFF"); c2 = (Color)ColorConverter.ConvertFromString("#5856D6"); c3 = (Color)ColorConverter.ConvertFromString("#FF2D55"); break;
            }

            GradStop1.Color = c1; GradStop2.Color = c2; GradStop3.Color = c3;
            Resources["AccentBrush"] = new SolidColorBrush(c1);
        }

        // --- ELENCO FRASI ---
        private void SalvaFrasiSuFile() => File.WriteAllText(phrasesPath, JsonSerializer.Serialize(tutteLeFrasi));

        private void CaricaFrasiDaFile()
        {
            if (File.Exists(phrasesPath))
            {
                string json = File.ReadAllText(phrasesPath);
                tutteLeFrasi = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            else
            {
                tutteLeFrasi = new List<string> { "Dormire", "Casa", "Oggi è una splendida giornata" };
            }
            AggiornaListaMostrata();
        }

        private void AggiungiFrase()
        {
            string testo = TxtNuovaFrase.Text.Trim();
            if (!string.IsNullOrEmpty(testo) && testo != "Scrivi una nuova parola e premi Invio...")
            {
                tutteLeFrasi.Add(testo);
                tutteLeFrasi.Sort();
                SalvaFrasiSuFile();
                TxtNuovaFrase.Text = "";
                AggiornaListaMostrata();
            }
        }

        private void BtnAggiungi_Click(object sender, RoutedEventArgs e) => AggiungiFrase();
        private void TxtNuovaFrase_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) AggiungiFrase(); }

        private void ListaFrasi_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListaFrasi.SelectedItem != null)
            {
                string frase = ListaFrasi.SelectedItem.ToString();
                if (MessageBox.Show($"Eliminare: \"{frase}\"?", "AlphabetZ", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    tutteLeFrasi.Remove(frase);
                    SalvaFrasiSuFile();
                    AggiornaListaMostrata();
                }
            }
        }

        private void InizializzaAlfabeto()
        {
            var alfabeto = new List<string> { "•" };
            alfabeto.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(c => c.ToString()));
            ListaAlfabeto.ItemsSource = alfabeto;
        }

        private void Lettera_Click(object sender, RoutedEventArgs e)
        {
            letteraCorrente = (sender as Button).Content.ToString();
            TxtIngestazioneLettera.Text = letteraCorrente == "•" ? " - Le Mie Parole" : $" - Lettera {letteraCorrente}";
            AggiornaListaMostrata();
        }

        private void AggiornaListaMostrata()
        {
            if (letteraCorrente == "•")
                ListaFrasi.ItemsSource = tutteLeFrasi.ToList();
            else
                ListaFrasi.ItemsSource = tutteLeFrasi.Where(f => f.StartsWith(letteraCorrente, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); }
        private void ChiudiApp_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}