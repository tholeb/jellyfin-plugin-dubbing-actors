import requests
import urllib.parse
import re
import argparse
import sys
import json

def log_info(message):
    """Envoie les logs sur stderr pour ne pas polluer le JSON sur stdout"""
    sys.stderr.write(message + "\n")

def get_vf_from_wikipedia(tmdb_id):
    headers = {'User-Agent': 'JellyfinVFScraper/4.0 (NAS TrueNAS script)'}

    # Structure de données finale
    output_data = {
        "tmdb_id": tmdb_id,
        "article_title": None,
        "cast": []
    }

    log_info(f"Recherche de l'article Wikipédia FR via Wikidata (TMDb ID: {tmdb_id})...")

    # L'UNION permet de chercher soit un Film (P4947) soit une Série TV (P4983)
    sparql_query = f"""
    SELECT ?article WHERE {{
      {{ ?item wdt:P4947 "{tmdb_id}" . }}
      UNION
      {{ ?item wdt:P4983 "{tmdb_id}" . }}
      ?article schema:about ?item ;
               schema:inLanguage "fr" ;
               schema:isPartOf <https://fr.wikipedia.org/> .
    }}
    """

    try:
        wd_response = requests.get('https://query.wikidata.org/sparql',
                                   params={'query': sparql_query, 'format': 'json'},
                                   headers=headers).json()
    except Exception as e:
        log_info(f"Erreur lors de la requête Wikidata : {e}")
        print(json.dumps(output_data, ensure_ascii=False, indent=2))
        return

    bindings = wd_response.get('results', {}).get('bindings', [])
    if not bindings:
        log_info("Aucun article Wikipédia FR trouvé pour cet ID TMDb.")
        print(json.dumps(output_data, ensure_ascii=False, indent=2))
        return

    article_url = bindings[0]['article']['value']
    article_title = urllib.parse.unquote(article_url.split('/')[-1])
    output_data["article_title"] = article_title.replace('_', ' ')

    log_info(f"Article trouvé : {output_data['article_title']}")
    log_info("Extraction du code source de la page...")

    wiki_api_url = "https://fr.wikipedia.org/w/api.php"
    params = {
        "action": "query",
        "prop": "revisions",
        "rvprop": "content",
        "rvslots": "main",
        "titles": article_title,
        "format": "json"
    }

    wiki_response = requests.get(wiki_api_url, params=params, headers=headers).json()
    pages = wiki_response.get('query', {}).get('pages', {})
    page = list(pages.values())[0]
    wikitext = page.get('revisions', [{}])[0].get('slots', {}).get('main', {}).get('*', '')

    # Recherche de la section "Voix françaises" (prend en compte == ou ===)
    match = re.search(r'==+\s*Voix françaises\s*==+\n(.*?)(?=\n==+|$)', wikitext, re.DOTALL | re.IGNORECASE)

    if not match:
        log_info("Section 'Voix françaises' introuvable dans le code de la page Wikipédia.")
        print(json.dumps(output_data, ensure_ascii=False, indent=2))
        return

    vf_text = match.group(1).strip()

    log_info("Nettoyage et structuration du casting...")

    for line in vf_text.split('\n'):
        if line.strip().startswith('*'):
            # 1. Nettoyage des balises de références
            clean_line = re.sub(r'<ref[^>]*>.*?</ref>', '', line, flags=re.IGNORECASE | re.DOTALL)
            clean_line = re.sub(r'<ref[^>]*/>', '', clean_line, flags=re.IGNORECASE)

            # 2. Nettoyage des liens wiki [[Lien|Texte]] -> Texte
            clean_line = re.sub(r'\[\[(?:[^|\]]*\|)?([^\]]+)\]\]', r'\1', clean_line)

            # 3. Nettoyage de la typographie wiki (gras, italique, puces)
            clean_line = clean_line.replace("'''", "").replace("''", "").replace("*", "").strip()

            # 4. Nettoyage des modèles {{modèle}} et des accolades isolées
            clean_line = re.sub(r'\{\{[^}]+\}\}', '', clean_line)
            clean_line = clean_line.replace('}}', '').replace('{{', '').strip()

            # 5. Remplacement des espaces insécables HTML
            clean_line = clean_line.replace('&nbsp;', ' ')

            if clean_line:
                # Séparation Acteur / Rôle
                if ':' in clean_line:
                    parts = clean_line.split(':', 1)
                    actor = parts[0].strip()
                    role = parts[1].strip()
                else:
                    actor = clean_line
                    role = ""

                output_data["cast"].append({
                    "actor": actor,
                    "role": role
                })

    # Sortie JSON standardisée sur stdout
    print(json.dumps(output_data, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Récupère le casting VF depuis Wikipédia au format JSON.")
    parser.add_argument("tmdb_id", help="L'ID TMDb du film ou de la série (ex: 260514)")
    args = parser.parse_args()

    get_vf_from_wikipedia(args.tmdb_id)
