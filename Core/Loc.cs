using System.ComponentModel;
using System.Globalization;

namespace CodeSearch.Core;

/// <summary>
/// UI localization. XAML binds to the indexer: {Binding [key], Source={x:Static core:Loc.I}};
/// changing the language raises "Item[]" so every bound string refreshes at once.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly Loc I = new();
    public event PropertyChangedEventHandler PropertyChanged;
    public static event Action LanguageChanged;

    public static readonly (string Code, string Name)[] Languages =
    {
        ("tr", "Türkçe"), ("en", "English"), ("de", "Deutsch"), ("es", "Español"), ("fr", "Français")
    };

    static int _col = 1; // index into the translation arrays (order of Languages)
    public static string Lang => Languages[_col].Code;

    public string this[string key] => T(key);

    public static string T(string key)
    {
        if (!S.TryGetValue(key, out var a)) return key;
        return a[_col] ?? a[1] ?? key;
    }

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>"" = follow the Windows display language (falls back to English).</summary>
    public static string Resolve(string code)
    {
        if (!string.IsNullOrEmpty(code) && Languages.Any(l => l.Code == code)) return code;
        var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == sys) ? sys : "en";
    }

    public static void Set(string code)
    {
        code = Resolve(code);
        _col = Array.FindIndex(Languages, l => l.Code == code);
        var ci = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = ci;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = ci;
        I.PropertyChanged?.Invoke(I, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    //                                   tr, en, de, es, fr
    static readonly Dictionary<string, string[]> S = new()
    {
        ["lang"] = new[] { "Dil", "Language", "Sprache", "Idioma", "Langue" },
        ["admin_suffix"] = new[] { "(Yönetici)", "(Administrator)", "(Administrator)", "(Administrador)", "(Administrateur)" },

        // top bar
        ["mode_name"] = new[] { "Dosya / Klasör adı", "File / folder name", "Datei-/Ordnername", "Nombre de archivo/carpeta", "Nom de fichier/dossier" },
        ["mode_name_tip"] = new[] { "Dosya ve klasör adlarında ara (Ctrl+1)", "Search file and folder names (Ctrl+1)", "In Datei- und Ordnernamen suchen (Strg+1)", "Buscar en nombres de archivos y carpetas (Ctrl+1)", "Rechercher dans les noms de fichiers et dossiers (Ctrl+1)" },
        ["mode_content"] = new[] { "Kod içeriği", "Code content", "Codeinhalt", "Contenido del código", "Contenu du code" },
        ["mode_content_tip"] = new[] { "Kod dosyalarının içeriğinde ara (Ctrl+2)", "Search inside code files (Ctrl+2)", "In Codedateien suchen (Strg+2)", "Buscar dentro de archivos de código (Ctrl+2)", "Rechercher dans les fichiers de code (Ctrl+2)" },
        ["settings_btn"] = new[] { "⚙  Tarama / Ayarlar", "⚙  Scan / Settings", "⚙  Scan / Einstellungen", "⚙  Escaneo / Ajustes", "⚙  Analyse / Paramètres" },
        ["settings_tip"] = new[] { "Disk seçimi, boyut limiti ve yeniden tarama", "Drive selection, size limit and rescan", "Laufwerksauswahl, Größenlimit und Neuscan", "Selección de unidades, límite de tamaño y reescaneo", "Choix des disques, limite de taille et nouvelle analyse" },
        ["ph_name"] = new[] { "Dosya veya klasör adı ara...  (örn: *.cs player, ext:js config)", "Search file or folder names...  (e.g. *.cs player, ext:js config)", "Datei- oder Ordnernamen suchen...  (z. B. *.cs player, ext:js config)", "Buscar nombres de archivos o carpetas...  (p. ej. *.cs player, ext:js config)", "Rechercher des noms de fichiers ou dossiers...  (ex. *.cs player, ext:js config)" },
        ["ph_content"] = new[] { "Kod içeriğinde ara...  (örn: GetComponent, async Task, TODO)", "Search code content...  (e.g. GetComponent, async Task, TODO)", "Im Code suchen...  (z. B. GetComponent, async Task, TODO)", "Buscar en el código...  (p. ej. GetComponent, async Task, TODO)", "Rechercher dans le code...  (ex. GetComponent, async Task, TODO)" },

        // options
        ["opt_case"] = new[] { "Büyük/küçük harf duyarlı", "Match case", "Groß-/Kleinschreibung", "Distinguir mayúsculas", "Respecter la casse" },
        ["opt_word"] = new[] { "Tam kelime", "Whole word", "Ganzes Wort", "Palabra completa", "Mot entier" },
        ["opt_regex"] = new[] { "Regex", "Regex", "Regex", "Regex", "Regex" },
        ["file_filter"] = new[] { "Dosya filtresi:", "File filter:", "Dateifilter:", "Filtro de archivos:", "Filtre de fichiers :" },
        ["file_filter_tip"] = new[] { "Örn:  *.cs;*.js   veya   src  !test   (uzantılar VEYA, kelimeler yolda geçmeli, ! hariç tutar)", "E.g.  *.cs;*.js   or   src  !test   (extensions are OR-ed, words must appear in the path, ! excludes)", "Z. B.  *.cs;*.js   oder   src  !test   (Endungen ODER-verknüpft, Wörter müssen im Pfad vorkommen, ! schließt aus)", "P. ej.  *.cs;*.js   o   src  !test   (extensiones con O, las palabras deben estar en la ruta, ! excluye)", "Ex.  *.cs;*.js   ou   src  !test   (extensions en OU, les mots doivent figurer dans le chemin, ! exclut)" },
        ["help_name"] = new[] { "İpucu:  kelime1 kelime2 (VE)   !hariç   *.cs   src\\util   ext:cs;js   file:   folder:", "Tip:  word1 word2 (AND)   !exclude   *.cs   src\\util   ext:cs;js   file:   folder:", "Tipp:  wort1 wort2 (UND)   !ausschließen   *.cs   src\\util   ext:cs;js   file:   folder:", "Consejo:  palabra1 palabra2 (Y)   !excluir   *.cs   src\\util   ext:cs;js   file:   folder:", "Astuce :  mot1 mot2 (ET)   !exclure   *.cs   src\\util   ext:cs;js   file:   folder:" },
        ["help_content"] = new[] { "Enter: VS Code'da satırda aç   •   Ctrl+Enter: klasörde göster", "Enter: open at line in VS Code   •   Ctrl+Enter: show in folder", "Enter: in VS Code an der Zeile öffnen   •   Strg+Enter: im Ordner anzeigen", "Enter: abrir en la línea en VS Code   •   Ctrl+Enter: mostrar en carpeta", "Entrée : ouvrir à la ligne dans VS Code   •   Ctrl+Entrée : afficher dans le dossier" },

        // columns
        ["col_name"] = new[] { "Ad", "Name", "Name", "Nombre", "Nom" },
        ["col_folder"] = new[] { "Klasör", "Folder", "Ordner", "Carpeta", "Dossier" },
        ["col_size"] = new[] { "Boyut", "Size", "Größe", "Tamaño", "Taille" },
        ["col_modified"] = new[] { "Değiştirilme", "Modified", "Geändert", "Modificado", "Modifié" },
        ["col_file"] = new[] { "Dosya", "File", "Datei", "Archivo", "Fichier" },
        ["col_line"] = new[] { "Satır", "Line", "Zeile", "Línea", "Ligne" },
        ["col_content"] = new[] { "İçerik", "Content", "Inhalt", "Contenido", "Contenu" },

        // context menu
        ["ctx_open"] = new[] { "Aç", "Open", "Öffnen", "Abrir", "Ouvrir" },
        ["ctx_code"] = new[] { "VS Code ile aç", "Open in VS Code", "In VS Code öffnen", "Abrir en VS Code", "Ouvrir dans VS Code" },
        ["ctx_code_line"] = new[] { "VS Code ile satırda aç", "Open at line in VS Code", "In VS Code an der Zeile öffnen", "Abrir en la línea en VS Code", "Ouvrir à la ligne dans VS Code" },
        ["ctx_open_default"] = new[] { "Varsayılan uygulamayla aç", "Open with default app", "Mit Standard-App öffnen", "Abrir con la aplicación predeterminada", "Ouvrir avec l'application par défaut" },
        ["ctx_reveal"] = new[] { "Klasörde göster", "Show in folder", "Im Ordner anzeigen", "Mostrar en carpeta", "Afficher dans le dossier" },
        ["ctx_copy_path"] = new[] { "Tam yolu kopyala", "Copy full path", "Vollständigen Pfad kopieren", "Copiar ruta completa", "Copier le chemin complet" },
        ["ctx_copy_name"] = new[] { "Adı kopyala", "Copy name", "Namen kopieren", "Copiar nombre", "Copier le nom" },
        ["ctx_copy_line"] = new[] { "Satırı kopyala", "Copy line", "Zeile kopieren", "Copiar línea", "Copier la ligne" },

        // preview
        ["preview"] = new[] { "Önizleme", "Preview", "Vorschau", "Vista previa", "Aperçu" },
        ["preview_title"] = new[] { "{0}   —   satır {1}   •   {2}", "{0}   —   line {1}   •   {2}", "{0}   —   Zeile {1}   •   {2}", "{0}   —   línea {1}   •   {2}", "{0}   —   ligne {1}   •   {2}" },
        ["plain_text"] = new[] { "düz metin", "plain text", "Klartext", "texto plano", "texte brut" },
        ["cannot_read"] = new[] { "(dosya okunamadı)", "(file could not be read)", "(Datei konnte nicht gelesen werden)", "(no se pudo leer el archivo)", "(impossible de lire le fichier)" },

        // setup overlay
        ["setup_title"] = new[] { "Tarama ayarları", "Scan settings", "Scan-Einstellungen", "Ajustes de escaneo", "Paramètres d'analyse" },
        ["setup_desc"] = new[] {
            "Taranacak diskleri ve içerik indekslemede kullanılacak maksimum dosya boyutunu seçin. Dosya/klasör adları her zaman tamamen indekslenir; içerik indeksine yalnızca aşağıdaki uzantılara sahip ve boyut limitinin altındaki dosyalar alınır.",
            "Choose the drives to scan and the maximum file size for content indexing. File and folder names are always fully indexed; only files with the extensions below and under the size limit go into the content index.",
            "Wählen Sie die zu scannenden Laufwerke und die maximale Dateigröße für die Inhaltsindizierung. Datei- und Ordnernamen werden immer vollständig indiziert; in den Inhaltsindex kommen nur Dateien mit den unten genannten Endungen unterhalb des Größenlimits.",
            "Elija las unidades a escanear y el tamaño máximo de archivo para indexar contenido. Los nombres de archivos y carpetas siempre se indexan por completo; en el índice de contenido solo entran archivos con las extensiones de abajo y por debajo del límite de tamaño.",
            "Choisissez les disques à analyser et la taille maximale des fichiers pour l'indexation du contenu. Les noms de fichiers et dossiers sont toujours entièrement indexés ; seuls les fichiers ayant les extensions ci-dessous et sous la limite de taille entrent dans l'index de contenu." },
        ["drives"] = new[] { "Diskler", "Drives", "Laufwerke", "Unidades", "Disques" },
        ["max_size"] = new[] { "İçerik indeksi için maksimum dosya boyutu", "Maximum file size for the content index", "Maximale Dateigröße für den Inhaltsindex", "Tamaño máximo de archivo para el índice de contenido", "Taille maximale des fichiers pour l'index de contenu" },
        ["extensions"] = new[] { "İçeriği indekslenecek uzantılar (; ile ayırın)", "Extensions whose content is indexed (separate with ;)", "Endungen, deren Inhalt indiziert wird (mit ; trennen)", "Extensiones cuyo contenido se indexa (separar con ;)", "Extensions dont le contenu est indexé (séparer par ;)" },
        ["excludes"] = new[] { "İçerik indeksinden hariç tutulan klasörler (ad veya tam yol, ; ile ayırın)", "Folders excluded from the content index (name or full path, separate with ;)", "Vom Inhaltsindex ausgeschlossene Ordner (Name oder vollständiger Pfad, mit ; trennen)", "Carpetas excluidas del índice de contenido (nombre o ruta completa, separar con ;)", "Dossiers exclus de l'index de contenu (nom ou chemin complet, séparer par ;)" },
        ["admin_btn"] = new[] { "🛡  Yönetici olarak yeniden başlat (hızlı MFT tarama)", "🛡  Restart as administrator (fast MFT scan)", "🛡  Als Administrator neu starten (schneller MFT-Scan)", "🛡  Reiniciar como administrador (escaneo MFT rápido)", "🛡  Redémarrer en administrateur (analyse MFT rapide)" },
        ["admin_tip"] = new[] { "Yönetici olarak NTFS MFT doğrudan okunur: milyonlarca dosya saniyeler içinde taranır (Everything gibi).", "As administrator the NTFS MFT is read directly: millions of files are scanned in seconds (like Everything).", "Als Administrator wird die NTFS-MFT direkt gelesen: Millionen Dateien in Sekunden (wie Everything).", "Como administrador se lee directamente la MFT de NTFS: millones de archivos en segundos (como Everything).", "En administrateur, la MFT NTFS est lue directement : des millions de fichiers en quelques secondes (comme Everything)." },
        ["close"] = new[] { "Kapat", "Close", "Schließen", "Cerrar", "Fermer" },
        ["cancel_scan"] = new[] { "Taramayı iptal et", "Cancel scan", "Scan abbrechen", "Cancelar escaneo", "Annuler l'analyse" },
        ["start_scan"] = new[] { "▶  Taramayı başlat", "▶  Start scan", "▶  Scan starten", "▶  Iniciar escaneo", "▶  Lancer l'analyse" },
        ["rescan"] = new[] { "↻  Yeniden tara", "↻  Rescan", "↻  Neu scannen", "↻  Volver a escanear", "↻  Nouvelle analyse" },
        ["drive_used"] = new[] { "{0} / {1} dolu", "{0} / {1} used", "{0} / {1} belegt", "{0} / {1} usado", "{0} / {1} utilisés" },
        ["drv_Fixed"] = new[] { "Yerel disk", "Local disk", "Lokaler Datenträger", "Disco local", "Disque local" },
        ["drv_Removable"] = new[] { "Çıkarılabilir", "Removable", "Wechseldatenträger", "Extraíble", "Amovible" },
        ["drv_Network"] = new[] { "Ağ sürücüsü", "Network drive", "Netzlaufwerk", "Unidad de red", "Lecteur réseau" },
        ["drv_CDRom"] = new[] { "CD/DVD", "CD/DVD", "CD/DVD", "CD/DVD", "CD/DVD" },
        ["drv_Ram"] = new[] { "RAM disk", "RAM disk", "RAM-Disk", "Disco RAM", "Disque RAM" },
        ["pick_drive"] = new[] { "En az bir disk seçin.", "Select at least one drive.", "Wählen Sie mindestens ein Laufwerk.", "Seleccione al menos una unidad.", "Sélectionnez au moins un disque." },
        ["bad_kb"] = new[] { "Geçerli bir KB değeri girin.", "Enter a valid KB value.", "Geben Sie einen gültigen KB-Wert ein.", "Introduzca un valor de KB válido.", "Saisissez une valeur en Ko valide." },

        // status bar
        ["loading_index"] = new[] { "İndeks yükleniyor...", "Loading index...", "Index wird geladen...", "Cargando índice...", "Chargement de l'index..." },
        ["no_index_yet"] = new[] { "Henüz indeks yok. Diskleri seçip taramayı başlatın.", "No index yet. Select drives and start a scan.", "Noch kein Index. Laufwerke wählen und Scan starten.", "Aún no hay índice. Elija unidades e inicie el escaneo.", "Pas encore d'index. Choisissez des disques et lancez l'analyse." },
        ["index_loaded"] = new[] { "İndeks {0} ms'de yüklendi. Aramaya hazır.", "Index loaded in {0} ms. Ready to search.", "Index in {0} ms geladen. Bereit zur Suche.", "Índice cargado en {0} ms. Listo para buscar.", "Index chargé en {0} ms. Prêt pour la recherche." },
        ["type_to_search"] = new[] { "Aramak için yazın.", "Type to search.", "Tippen zum Suchen.", "Escriba para buscar.", "Tapez pour rechercher." },
        ["no_index"] = new[] { "İndeks yok", "No index", "Kein Index", "Sin índice", "Aucun index" },
        ["name_results"] = new[] { "{0:N0} sonuç", "{0:N0} results", "{0:N0} Ergebnisse", "{0:N0} resultados", "{0:N0} résultats" },
        ["shown_first"] = new[] { " (ilk {0:N0} gösteriliyor)", " (showing first {0:N0})", " (erste {0:N0} angezeigt)", " (mostrando los primeros {0:N0})", " (affichage des {0:N0} premiers)" },
        ["content_results"] = new[] { "{0:N0} eşleşme, {1:N0} dosya", "{0:N0} matches in {1:N0} files", "{0:N0} Treffer in {1:N0} Dateien", "{0:N0} coincidencias en {1:N0} archivos", "{0:N0} correspondances dans {1:N0} fichiers" },
        ["truncated"] = new[] { " (limit aşıldı, ilk sonuçlar gösteriliyor)", " (limit reached, showing first results)", " (Limit erreicht, erste Ergebnisse)", " (límite alcanzado, primeros resultados)", " (limite atteinte, premiers résultats)" },
        ["searched_in"] = new[] { "{0:N0} MB içinde {1:N1} ms", "{1:N1} ms across {0:N0} MB", "{1:N1} ms in {0:N0} MB", "{1:N1} ms en {0:N0} MB", "{1:N1} ms sur {0:N0} Mo" },
        ["invalid_regex"] = new[] { "Geçersiz regex: {0}", "Invalid regex: {0}", "Ungültiger Regex: {0}", "Regex no válida: {0}", "Regex invalide : {0}" },
        ["invalid_search"] = new[] { "Geçersiz arama: {0}", "Invalid search: {0}", "Ungültige Suche: {0}", "Búsqueda no válida: {0}", "Recherche invalide : {0}" },
        ["open_failed"] = new[] { "Açılamadı", "Could not open", "Konnte nicht geöffnet werden", "No se pudo abrir", "Impossible d'ouvrir" },
        ["exit_scanning"] = new[] { "Tarama devam ediyor. Çıkılsın mı?", "A scan is running. Quit anyway?", "Ein Scan läuft. Trotzdem beenden?", "Hay un escaneo en curso. ¿Salir de todos modos?", "Une analyse est en cours. Quitter quand même ?" },
        ["idx_status"] = new[] { "{0:N0} dosya, {1:N0} klasör", "{0:N0} files, {1:N0} folders", "{0:N0} Dateien, {1:N0} Ordner", "{0:N0} archivos, {1:N0} carpetas", "{0:N0} fichiers, {1:N0} dossiers" },
        ["idx_code"] = new[] { "{0:N0} kod dosyası ({1:N0} MB, ≤{2:N0} KB)", "{0:N0} code files ({1:N0} MB, ≤{2:N0} KB)", "{0:N0} Codedateien ({1:N0} MB, ≤{2:N0} KB)", "{0:N0} archivos de código ({1:N0} MB, ≤{2:N0} KB)", "{0:N0} fichiers de code ({1:N0} Mo, ≤{2:N0} Ko)" },
        ["scan_done"] = new[] { "Tamamlandı: {0:N1} sn (adlar {1:N1} sn{2}, içerik {3:N1} sn).", "Done: {0:N1} s (names {1:N1} s{2}, content {3:N1} s).", "Fertig: {0:N1} s (Namen {1:N1} s{2}, Inhalt {3:N1} s).", "Terminado: {0:N1} s (nombres {1:N1} s{2}, contenido {3:N1} s).", "Terminé : {0:N1} s (noms {1:N1} s{2}, contenu {3:N1} s)." },
        ["scan_cancelled"] = new[] { "Tarama iptal edildi. Önceki indeks kullanılmaya devam ediyor.", "Scan cancelled. The previous index is still in use.", "Scan abgebrochen. Der vorherige Index bleibt aktiv.", "Escaneo cancelado. Se sigue usando el índice anterior.", "Analyse annulée. L'index précédent reste utilisé." },
        ["scan_error"] = new[] { "Tarama hatası: {0}", "Scan error: {0}", "Scan-Fehler: {0}", "Error de escaneo: {0}", "Erreur d'analyse : {0}" },
        ["deleted"] = new[] { "(silinmiş)", "(deleted)", "(gelöscht)", "(eliminado)", "(supprimé)" },

        // scan progress (Core)
        ["p_mft"] = new[] { "{0} MFT okunuyor... {1:N0} kayıt", "{0} reading MFT... {1:N0} records", "{0} MFT wird gelesen... {1:N0} Einträge", "{0} leyendo MFT... {1:N0} registros", "{0} lecture de la MFT... {1:N0} entrées" },
        ["p_dirs"] = new[] { "{0} klasörler taranıyor... {1:N0} öğe", "{0} scanning folders... {1:N0} items", "{0} Ordner werden gescannt... {1:N0} Elemente", "{0} escaneando carpetas... {1:N0} elementos", "{0} analyse des dossiers... {1:N0} éléments" },
        ["p_build_names"] = new[] { "İsim indeksi oluşturuluyor...", "Building name index...", "Namensindex wird erstellt...", "Creando índice de nombres...", "Création de l'index des noms..." },
        ["p_pick_files"] = new[] { "Kod dosyaları belirleniyor...", "Selecting code files...", "Codedateien werden ermittelt...", "Seleccionando archivos de código...", "Sélection des fichiers de code..." },
        ["p_content"] = new[] { "İçerik indeksleniyor: {0:N0} / {1:N0} dosya  •  {2:N0} indekslendi  •  {3:N0} MB", "Indexing content: {0:N0} / {1:N0} files  •  {2:N0} indexed  •  {3:N0} MB", "Inhalt wird indiziert: {0:N0} / {1:N0} Dateien  •  {2:N0} indiziert  •  {3:N0} MB", "Indexando contenido: {0:N0} / {1:N0} archivos  •  {2:N0} indexados  •  {3:N0} MB", "Indexation du contenu : {0:N0} / {1:N0} fichiers  •  {2:N0} indexés  •  {3:N0} Mo" },
        ["p_loading"] = new[] { "İndeks yükleniyor...", "Loading index...", "Index wird geladen...", "Cargando índice...", "Chargement de l'index..." },
    };
}
