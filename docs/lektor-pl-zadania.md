# Lektor PL — stan zadań wersji 5.1

Aktualizacja: 18 października 2026, według decyzji o osobnych funkcjach.
Punkty 5.1a i 5.3 są zaimplementowane w wersji 5.1; D1 pozostaje do wykonania.

## Ustalony przebieg pracy

| Etap użytkownika | Funkcja | Wynik | Stan |
| --- | --- | --- | --- |
| 1 | Masowe SRT → WAV PL | Osobny zsynchronizowany WAV dla każdego SRT | Działa w 5.0 |
| 1a | Połączenie dwóch częściowych WAV/AC3 PL | Jeden kompletny WAV PL | Gotowe w 5.1, zadanie 5.1a |
| 2 | Masowe miksowanie WAV PL z audio wybranym z wideo | Gotowy MIX PL, np. AC3 | Gotowe w 5.1, zadanie 5.3, osobne okno |
| 3 | Masowe muksowanie wideo + gotowy MIX PL + opcjonalne wymuszone SRT PL | MKV | Gotowe w 5.0 |

Masowe miksowanie audio ma otrzymać własne okno i korzystać z działającego
mechanizmu pojedynczego Lektor PL. Obecny prosty moduł masowego MKV pozostaje
przeznaczony do gotowych miksów. W jego tabeli nie dodajemy przełączania między
surowym WAV a gotowym miksem ani etapu wcześniejszego miksowania.

## 5.1a — NOWE: połączenie dwóch częściowych WAV lektora PL

**Status: zaimplementowane w 5.1.** Prosta, osobna funkcja dostępna przy module WAV
oraz z górnej belki programu.

### Okno i wynik

Praktyczny przykład dostarczony przez użytkownika zawiera dwie części w AC3,
po 23:24,960, 48 kHz, stereo, 224 kb/s. Dlatego funkcja ma akceptować zarówno
WAV, jak i AC3 czystego lektora; wynik nadal zapisuje jako jeden WAV PCM.
Nazwy w oknie powinny jasno pokazywać obsługiwane formaty.

- Pola wyboru dwóch plików: **Lektor PL — część 1 (WAV/AC3)** i **Lektor PL — część 2 (WAV/AC3)**.
- Wybór katalogu wynikowego i widoczna proponowana nazwa końcowego WAV.
- Przycisk **Połącz WAV**, postęp, możliwość zatrzymania oraz komunikat wyniku.
- Jeden wynikowy plik WAV, gotowy do następnego etapu miksowania z oryginałem.
- Pliki źródłowe pozostają zachowane, okno po zakończeniu jest gotowe do użycia.

### Najważniejsza zasada: wspólna oś czasu

Funkcja jest przeznaczona do częściowych plików odzyskanych z generowania TTS,
które zachowują oryginalne pozycje wypowiedzi. Oba wejścia zaczynają się na osi
czasu od 00:00; wypowiedzi jednej części uzupełniają ciszę drugiej.

- Połączyć audio na tej samej osi czasu, bez doklejania drugiego pliku za końcem
  pierwszego. Długość wyniku odpowiada dłuższemu wejściu, nie sumie długości.
- Zachować początkową i wewnętrzną ciszę, pozycje wypowiedzi i przerwy.
- Nie uruchamiać TTS ponownie. Operacja nie zużywa znaków usługi.
- Nie wykonywać ponownej korekty tempa, przycinania ciszy ani automatycznego
  przesuwania początku drugiej części do miejsca, w którym zaczyna ona mówić.
- Wykorzystać istniejące mechanizmy łączenia audio i FFmpeg. Zapis końcowy jako
  WAV PCM; w razie różnego formatu technicznego wejść sprowadzić je do wspólnego
  formatu z zachowaniem pozycji czasowych oraz poziomu pojedynczej wypowiedzi.
- Nie stosować automatycznego uśredniania, które ściszyłoby lektora o połowę
  tylko dlatego, że drugi plik jest w danym miejscu cichy.

Przykład: plik A ma pierwszych 118 kwestii i ciszę w dalszej części; plik B ma
ciszę do czasu pierwszej brakującej kwestii i dalszy lektor. Wynik ma całą mowę
na pierwotnych pozycjach, około 23:25, zamiast dwóch odrębnych ścieżek.

Sam plik audio bez zachowanych początkowych odstępów nie podaje pierwotnego czasu
wypowiedzi. Funkcja nie może zgadywać przesunięcia dla ręcznie przyciętych klipów.

### Nazwa wyniku

Reguła wynikająca z wymagania „bez dopisków po znaku _”: część nazwy pliku
przed pierwszym `_`, z rozszerzeniem `.wav`. Jeśli `_` nie ma, zachować nazwę
bazową. Proponowana nazwa jest widoczna przed uruchomieniem.

Przykład:

- `S02E25_LEKTOR_PL_MarekNeural_v1.7_czesciowy.wav`
- `S02E25_LEKTOR_PL_MarekNeural_v1.7_pozostale.wav`
- wynik: **`S02E25.wav`** we wskazanym katalogu.

Sprawdzić, czy wejścia stanowią właściwą parę, nie są tym samym plikiem i czy
wynik nie zastąpi wejścia ani istniejącego wyniku. Przy istotnej jednoczesnej
mowie w obu plikach trzeba wskazać, że części nie są komplementarne; nie wolno
samodzielnie wybierać, którą wypowiedź nadpisać lub usunąć. Nie wprowadzać
automatycznego montażu treści na podstawie samej nazwy pliku.

### Kryteria odbioru

- Dwa komplementarne pliki tworzą jeden WAV z poprawnym początkiem, środkiem
  i końcem; czas wyniku jest równy dłuższemu wejściu.
- Pierwsze wypowiedzi drugiej części nie przesuwają się na początek filmu.
- Zachowany poziom głosu; brak obcięcia pierwszej lub ostatniej kwestii.
- Prawidłowa nazwa przed pierwszym `_`, obsługa katalogu i kolizji wyników.
- Żadne wywołanie TTS ani zmiana licznika znaków nie następuje przy scalaniu.

## 5.3 — PILNE: osobny moduł masowego miksowania audio MIX PL

**Status: zaimplementowane w 5.1.** Nowe, oddzielne, zmaksymalizowane
okno, przeznaczone wyłącznie do tworzenia gotowych miksów audio.

### Wejścia i tabela

- Wskazanie katalogu filmów, z których pobierana będzie oryginalna ścieżka audio.
- Wskazanie katalogu czystych, zsynchronizowanych WAV lektora PL.
- Niezależne alfabetyczne uporządkowanie każdej kolumny i wiązanie według wierszy,
  jak w istniejącym masowym muksowaniu.
- Tabela: **Lp. / Wideo źródłowe / Wybrana oryginalna ścieżka audio / WAV lektora
  PL / Wynikowy MIX PL / Stan**.
- Przy kilku oryginalnych ścieżkach w filmie umożliwić jawny wybór właściwej
  ścieżki dla danego wiersza, z informacją o jej języku/parametrach.
- Zachować obsługę kolejności: zamiana komórek przez przeciąganie, wstawienie
  pustej z przesunięciem jednej kolumny w dół, usunięcie komórki z podniesieniem
  tej kolumny i usunięcie całego wiersza. Zmiana filmu wymaga ponownego wyboru
  lub sprawdzenia jego ścieżki audio, aby indeks nie wskazywał ścieżki innego filmu.

### Wykorzystanie działającego mechanizmu

- Dla każdej pary wywołać istniejącą operację **Tylko miks** z pojedynczego
  Lektor PL: wybrana ścieżka oryginalna z filmu + WAV lektora PL.
- Zachować dotychczasowy algorytm ściszania oryginału, sumowania i limitera;
  nie budować zamiennika. Obecna usługa już obsługuje zapis samego miksu audio.
- Wspólne ustawienia kolejki odpowiadają pojedynczemu Lektor PL: tryb
  stały/dynamiczny, poziom oryginału i lektora, próg, ratio, attack/release,
  kodek oraz bitrate. Zachować obsługę AC3 i pozostałych obecnych kodeków.
- Jeden wskazany katalog dla wszystkich wyników. Widoczna nazwa miksu każdego
  wiersza, np. `nazwa_wideo_MiksPL.ac3`.
- Moduł zapisuje wyłącznie audio. Nie tworzy MKV, nie przetwarza napisów,
  nie wymaga SRT ani MKVToolNix i nie uruchamia syntezy TTS.
- Wejściem jest WAV lektora; nie ma kolumny alternatywnego gotowego miksu.
  Gotowe miksy należą do następnego, istniejącego etapu tworzenia MKV.

### Kontrola i kryteria odbioru

- Sprawdzić całą kolejkę przed pierwszym miksem: komplet par, istnienie plików,
  poprawność WAV i wskazanych oryginalnych ścieżek audio, katalog wynikowy,
  powtórzone/niejednoznaczne przypisania oraz kolizje nazw.
- Nie nadpisywać źródeł. Postęp i stan każdego wiersza mają być widoczne.
- Przetwarzać kolejno. Stop/błąd zatrzymuje następne wiersze; ukończone wyniki
  pozostają, a pliki tymczasowe przerwanej operacji są sprzątane.
- Po zakończeniu pozostawić okno otwarte do kolejnego zestawu.
- Porównać operację masową i pojedyncze **Tylko miks** dla tych samych par
  i ustawień: długość, synchronizacja, kodek, kanały, poziomy i ściszanie tła.
- Wynikowy MIX PL ma nadawać się bezpośrednio do obecnego modułu masowego MKV.

## 5.2 — gotowe: masowe tworzenie MKV

Pozostawić dotychczasowy zakres i proste wejścia: **wideo + gotowy MIX PL +
opcjonalne wymuszone napisy PL → MKV**. Zachować powiązanie według wierszy,
flagę języka PL, default/forced dodanych napisów oraz dotychczasową kontrolę
wynikowych ścieżek i obrazu. Nie dodawać tu tworzenia miksu z czystego WAV.

Kolejność pracy wynika więc z trzech osobnych etapów: WAV PL (ewentualnie
połączony z dwóch odzyskanych części), następnie MIX PL, następnie MKV.

## D1 — diagnostyka przerwań TTS ustalona podczas analizy

**Status: do wykonania; bez zmiany rozliczania znaków i odzyskiwania audio.**

W logu zapisywać również powód zatrzymania i wynik ratowania pracy: rzeczywistą
wersję/build, plik zadania, indeks ostatniej ukończonej i pierwszej nieukończonej
kwestii, rodzaj błędu, HTTP/status i identyfikator żądania, jeśli są dostępne,
czasy operacji oraz stan salda/rezerwacji bez kluczy API i innych sekretów.
Rozróżnić brak lokalnego salda, limit usługi, chwilowy throttling, problem
sieciowy/niepotwierdzone żądanie i zatrzymanie przez użytkownika. Nie wyciągać
wniosku o miesięcznym limicie wyłącznie z kodu 429.

Uzasadnienie: analiza rzeczywistego przerwania potwierdziła prawidłowy zapis
ukończonych fragmentów i pozostałego SRT, ale log nie zawierał końcowego powodu
zatrzymania. Ta informacja musi być możliwa do odczytania bez zgadywania.
