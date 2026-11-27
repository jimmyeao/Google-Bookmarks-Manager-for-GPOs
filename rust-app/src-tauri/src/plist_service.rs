use crate::models::BookmarkItem;
use regex::Regex;

/// Escapes XML special characters, mirroring .NET SecurityElement.Escape.
fn xml_escape(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
        .replace('\'', "&apos;")
}

/// Writes bookmarks as a macOS plist XML fragment (key + array), matching
/// the format Intune configuration profiles expect. This is a fragment, not
/// a full plist document.
pub fn save_to_plist_string(key_name: &str, bookmarks: &[BookmarkItem]) -> String {
    let mut out = String::new();
    out.push_str(&format!("<key>{}</key>\n", xml_escape(key_name)));
    out.push_str("<array>\n");
    for item in bookmarks {
        out.push_str("  <dict>\n");
        write_dict(&mut out, item, 2);
        out.push_str("  </dict>\n");
    }
    out.push_str("</array>\n");
    out
}

fn write_key_val(out: &mut String, pad: &str, key: &str, val: &Option<String>) {
    if let Some(v) = val {
        if !v.is_empty() {
            out.push_str(&format!("{}<key>{}</key>\n", pad, key));
            out.push_str(&format!("{}<string>{}</string>\n", pad, xml_escape(v)));
        }
    }
}

fn write_dict(out: &mut String, item: &BookmarkItem, indent: usize) {
    let pad = "  ".repeat(indent);

    write_key_val(out, &pad, "toplevel_name", &item.toplevel_name);
    write_key_val(out, &pad, "name", &item.name);
    write_key_val(out, &pad, "url", &item.url);

    if let Some(children) = &item.children {
        if !children.is_empty() {
            out.push_str(&format!("{}<key>children</key>\n", pad));
            out.push_str(&format!("{}<array>\n", pad));
            for child in children {
                out.push_str(&format!("{}  <dict>\n", pad));
                write_dict(out, child, indent + 2);
                out.push_str(&format!("{}  </dict>\n", pad));
            }
            out.push_str(&format!("{}</array>\n", pad));
        }
    }
}

/// Escapes bare '&' characters as '&amp;' while preserving existing entities
/// like '&amp;', '&#38;', '&lt;'. A valid entity is '&' followed by one or
/// more of [a-zA-Z0-9#] then ';'.
fn escape_bare_ampersands(content: &str) -> String {
    let mut out = String::with_capacity(content.len());
    let mut chars = content.char_indices().peekable();
    while let Some((i, c)) = chars.next() {
        if c != '&' {
            out.push(c);
            continue;
        }
        // Scan ahead: is this the start of a valid entity?
        let mut j = i + 1;
        let rest = &content[j..];
        let mut consumed = 0usize;
        for ch in rest.chars() {
            if ch.is_ascii_alphanumeric() || ch == '#' {
                consumed += ch.len_utf8();
            } else {
                break;
            }
        }
        j += consumed;
        let is_entity = consumed > 0 && content[j..].starts_with(';');
        if is_entity {
            out.push('&');
        } else {
            out.push_str("&amp;");
        }
    }
    out
}

/// Replaces bare '&' inside <string>...</string> with '&amp;' while
/// preserving existing entities — pasted URLs often contain unescaped
/// ampersands.
fn sanitize_xml_fragment(xml: &str) -> String {
    let string_re = Regex::new(r"(?s)(<string>)(.*?)(</string>)").unwrap();
    string_re
        .replace_all(xml, |caps: &regex::Captures| {
            let content = escape_bare_ampersands(&caps[2]);
            format!("{}{}{}", &caps[1], content, &caps[3])
        })
        .into_owned()
}

/// Parses a plist XML fragment (or a full plist document containing one)
/// back into bookmark items.
pub fn load_from_plist_string(xml: &str) -> Result<Vec<BookmarkItem>, String> {
    // If it's a full plist document, extract the first <array>...</array> body
    // so both fragments and complete profiles parse.
    let fragment = if xml.contains("<plist") {
        extract_first_array(xml).unwrap_or_else(|| xml.to_string())
    } else {
        xml.to_string()
    };

    let wrapped = format!("<root>{}</root>", fragment);
    // Fallback buffer for sanitized XML; must be declared here so the
    // parsed document can borrow it for the rest of the function.
    let sanitized_wrapped;
    let doc = match roxmltree::Document::parse(&wrapped) {
        Ok(d) => d,
        Err(_) => {
            let sanitized = sanitize_xml_fragment(&fragment);
            sanitized_wrapped = format!("<root>{}</root>", sanitized);
            roxmltree::Document::parse(&sanitized_wrapped)
                .map_err(|e| format!("Invalid plist XML: {}", e))?
        }
    };

    let root = doc.root_element();
    // Find the first top-level array element containing the bookmark dicts
    let array = root
        .children()
        .find(|n| n.is_element() && n.tag_name().name() == "array")
        .or_else(|| {
            // full plist doc structure: root > plist > dict > array — search deeper
            root.descendants()
                .find(|n| n.is_element() && n.tag_name().name() == "array")
        });

    let array = match array {
        Some(a) => a,
        None => return Ok(Vec::new()),
    };

    let mut items = Vec::new();
    for node in array.children() {
        if node.is_element() && node.tag_name().name() == "dict" {
            items.push(parse_dict(&node));
        }
    }
    Ok(items)
}

fn extract_first_array(xml: &str) -> Option<String> {
    // Finds the first <array> at the ManagedBookmarks/ManagedFavorites level:
    // use the first occurrence of "<array>" and its matching close by simple scan.
    let start = xml.find("<array>")?;
    let mut depth = 0usize;
    let bytes = xml.as_bytes();
    let mut i = start;
    while i < bytes.len() {
        if xml[i..].starts_with("<array>") {
            depth += 1;
            i += "<array>".len();
            continue;
        }
        if xml[i..].starts_with("</array>") {
            depth -= 1;
            i += "</array>".len();
            if depth == 0 {
                return Some(xml[start..i].to_string());
            }
            continue;
        }
        i += 1;
    }
    None
}

fn parse_dict(dict: &roxmltree::Node) -> BookmarkItem {
    let mut item = BookmarkItem::default();
    let elements: Vec<roxmltree::Node> = dict
        .children()
        .filter(|n| n.is_element())
        .collect();

    let mut i = 0;
    while i < elements.len() {
        let el = &elements[i];
        if el.tag_name().name() != "key" {
            i += 1;
            continue;
        }
        let key = el.text().unwrap_or("");
        let value_elem = elements.get(i + 1);
        if value_elem.is_none() {
            i += 1;
            continue;
        }
        let value_elem = value_elem.unwrap();

        if value_elem.tag_name().name() == "string" {
            let val = value_elem.text().unwrap_or("").to_string();
            match key {
                "toplevel_name" => item.toplevel_name = Some(val),
                "name" => item.name = Some(val),
                "url" => item.url = Some(val),
                _ => {}
            }
        } else if key == "children" && value_elem.tag_name().name() == "array" {
            let mut children = Vec::new();
            for child in value_elem.children() {
                if child.is_element() && child.tag_name().name() == "dict" {
                    children.push(parse_dict(&child));
                }
            }
            item.children = Some(children);
        }

        i += 2; // skip key + value
    }
    item
}

/// Extracts the toplevel_name from the first item that has one, mirroring
/// BookmarkModelConverter.ExtractTopLevelFolderName.
pub fn extract_top_level_name(items: &[BookmarkItem]) -> Option<String> {
    items
        .iter()
        .find_map(|i| i.toplevel_name.clone().filter(|s| !s.is_empty()))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn roundtrip_plist() {
        let items = vec![
            BookmarkItem {
                toplevel_name: Some("Company Bookmarks".into()),
                ..Default::default()
            },
            BookmarkItem {
                name: Some("Example".into()),
                url: Some("https://example.com?a=1&b=2".into()),
                ..Default::default()
            },
            BookmarkItem {
                name: Some("Folder".into()),
                children: Some(vec![BookmarkItem {
                    name: Some("Child".into()),
                    url: Some("https://child.example.com".into()),
                    ..Default::default()
                }]),
                ..Default::default()
            },
        ];
        let xml = save_to_plist_string("ManagedBookmarks", &items);
        let parsed = load_from_plist_string(&xml).unwrap();
        assert_eq!(parsed.len(), 3);
        assert_eq!(parsed[0].toplevel_name.as_deref(), Some("Company Bookmarks"));
        assert_eq!(parsed[1].url.as_deref(), Some("https://example.com?a=1&b=2"));
        let children = parsed[2].children.as_ref().unwrap();
        assert_eq!(children[0].name.as_deref(), Some("Child"));
    }

    #[test]
    fn parses_unescaped_ampersand() {
        let xml = r#"<key>ManagedBookmarks</key>
<array>
  <dict>
    <key>name</key>
    <string>Test</string>
    <key>url</key>
    <string>https://example.com?a=1&b=2</string>
  </dict>
</array>"#;
        let parsed = load_from_plist_string(xml).unwrap();
        assert_eq!(parsed[0].url.as_deref(), Some("https://example.com?a=1&b=2"));
    }
}
