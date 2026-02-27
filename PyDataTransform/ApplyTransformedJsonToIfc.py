import argparse
import json
from pathlib import Path


def _to_ifc_value(model, value):
    if value is None:
        return model.create_entity("IfcText", "")
    if isinstance(value, bool):
        return model.create_entity("IfcBoolean", value)
    if isinstance(value, int):
        return model.create_entity("IfcInteger", value)
    if isinstance(value, float):
        return model.create_entity("IfcReal", value)
    return model.create_entity("IfcText", str(value))


def _extract_guid(entry):
    props = entry.get("Properties")
    if not isinstance(props, list):
        return None
    for prop in props:
        if not isinstance(prop, dict):
            continue
        if str(prop.get("displayName", "")).strip().lower() == "guid":
            guid = prop.get("value")
            return str(guid).strip() if guid is not None else None
    return None


def _nominal_to_text(value):
    if value is None:
        return None
    if hasattr(value, "wrappedValue"):
        raw = value.wrappedValue
    else:
        raw = value
    if raw is None:
        return None
    return str(raw).strip()


def _get_psets(entity):
    psets = {}
    for rel in getattr(entity, "IsDefinedBy", []) or []:
        rel_def = getattr(rel, "RelatingPropertyDefinition", None)
        if rel_def is None or not rel_def.is_a("IfcPropertySet"):
            continue
        pset_name = getattr(rel_def, "Name", None)
        if not pset_name:
            continue
        psets[str(pset_name)] = rel_def
    return psets


def _extract_guid_from_entity_properties(entity):
    psets = _get_psets(entity)
    for pset in psets.values():
        for prop in getattr(pset, "HasProperties", []) or []:
            if not prop.is_a("IfcPropertySingleValue"):
                continue
            if str(getattr(prop, "Name", "")).strip().lower() != "guid":
                continue
            return _nominal_to_text(getattr(prop, "NominalValue", None))
    return None


def _build_entity_guid_index(model):
    guid_index = {}
    for entity in model.by_type("IfcObject"):
        guid_text = _extract_guid_from_entity_properties(entity)
        if not guid_text:
            continue
        guid_index.setdefault(guid_text.lower(), entity)
    return guid_index


def _guid_to_ifc_globalid(ifcopenshell_module, guid_value):
    if guid_value is None:
        return None
    text = str(guid_value).strip()
    compact_hex = text.replace("-", "")
    if len(compact_hex) != 32:
        return None
    try:
        return ifcopenshell_module.guid.compress(compact_hex)
    except Exception:
        return None


def _set_property_value(model, entity, category, prop_name, value):
    if not category or not prop_name:
        return False

    category_lower = str(category).strip().lower()
    prop_name_text = str(prop_name).strip()

    if prop_name_text.lower() == "name" and hasattr(entity, "Name"):
        entity.Name = "" if value is None else str(value)
        return True

    if category_lower == "ifc" and hasattr(entity, prop_name_text):
        try:
            setattr(entity, prop_name_text, value)
            return True
        except Exception:
            pass

    psets = _get_psets(entity)
    pset = psets.get(str(category))
    if pset is None:
        return False

    for p in getattr(pset, "HasProperties", []) or []:
        if not p.is_a("IfcPropertySingleValue"):
            continue
        if str(getattr(p, "Name", "")).strip() != prop_name_text:
            continue
        try:
            if getattr(p, "NominalValue", None) is not None and hasattr(p.NominalValue, "wrappedValue"):
                p.NominalValue.wrappedValue = value
            else:
                p.NominalValue = _to_ifc_value(model, value)
            return True
        except Exception:
            p.NominalValue = _to_ifc_value(model, value)
            return True

    return False


def apply_transformed_json_to_ifc(input_ifc_path, input_json_path, output_ifc_path):
    try:
        ifcopenshell = __import__("ifcopenshell")
    except ImportError as exc:
        raise RuntimeError("Missing dependency 'ifcopenshell'. Install it in the Python environment used by the app.") from exc

    model = ifcopenshell.open(str(input_ifc_path))
    data = json.loads(Path(input_json_path).read_text(encoding="utf-8"))
    if not isinstance(data, list):
        raise ValueError("Expected JSON array of model elements.")

    entity_guid_index = _build_entity_guid_index(model)

    elements_updated = 0
    properties_updated = 0

    for entry in data:
        if not isinstance(entry, dict):
            continue

        guid = _extract_guid(entry)
        if not guid:
            continue

        entity = None
        guid_candidates = [str(guid).strip()]
        global_id_candidate = _guid_to_ifc_globalid(ifcopenshell, guid)
        if global_id_candidate:
            guid_candidates.append(global_id_candidate)

        for guid_candidate in guid_candidates:
            try:
                entity = model.by_guid(guid_candidate)
            except Exception:
                entity = None
            if entity is not None:
                break
        if entity is None and guid:
            entity = entity_guid_index.get(str(guid).strip().lower())
        if entity is None:
            continue

        element_changed = False

        new_name = entry.get("Name")
        if hasattr(entity, "Name") and new_name is not None and str(getattr(entity, "Name", "")) != str(new_name):
            entity.Name = str(new_name)
            element_changed = True
            properties_updated += 1

        props = entry.get("Properties")
        if isinstance(props, list):
            for prop in props:
                if not isinstance(prop, dict):
                    continue
                category = prop.get("category")
                display_name = prop.get("displayName")
                value = prop.get("value")
                if _set_property_value(model, entity, category, display_name, value):
                    element_changed = True
                    properties_updated += 1

        if element_changed:
            elements_updated += 1

    output_ifc_path.parent.mkdir(parents=True, exist_ok=True)
    model.write(str(output_ifc_path))

    return {
        "elementsUpdated": elements_updated,
        "propertiesUpdated": properties_updated,
        "outputIfc": str(output_ifc_path),
    }


def main():
    parser = argparse.ArgumentParser(description="Apply transformed JSON metadata to IFC and write revised IFC.")
    parser.add_argument("--input-ifc", required=True)
    parser.add_argument("--input-json", required=True)
    parser.add_argument("--output-ifc", required=True)
    args = parser.parse_args()

    input_ifc = Path(args.input_ifc)
    input_json = Path(args.input_json)
    output_ifc = Path(args.output_ifc)

    if not input_ifc.exists():
        raise FileNotFoundError(f"Input IFC not found: {input_ifc}")
    if not input_json.exists():
        raise FileNotFoundError(f"Input JSON not found: {input_json}")

    result = apply_transformed_json_to_ifc(input_ifc, input_json, output_ifc)
    print(json.dumps(result))


if __name__ == "__main__":
    main()
