#include "json_project_serializer.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <map>
#include <string>
#include <vector>

namespace solar_sim {
namespace json {

enum class json_type { null, object, array, string, number, boolean };

struct json_value {
    json_type type = json_type::null;
    std::string string_value;
    double number_value = 0.0;
    bool bool_value = false;
    std::map<std::string, json_value> object_value;
    std::vector<json_value> array_value;
};

class parser {
public:
    static constexpr size_t max_string_len = 65536;
    static constexpr size_t max_array_size = 4096;
    static constexpr size_t max_object_size = 1024;
    static constexpr int max_depth = 64;

    parser(const char *input, size_t size, char *error, size_t error_size)
        : input_(input), size_(size), error_(error), error_size_(error_size) {}

    bool parse(json_value &out) {
        skip_ws();
        if (pos_ >= size_) {
            set_error("empty input");
            return false;
        }
        if (!parse_value(out)) return false;
        skip_ws();
        if (pos_ != size_) {
            set_error("trailing data after JSON value");
            return false;
        }
        return true;
    }

private:
    const char *input_;
    size_t size_;
    size_t pos_ = 0;
    int depth_ = 0;
    char *error_;
    size_t error_size_;

    void set_error(const char *msg) {
        if (error_ && error_size_ > 0) {
            std::snprintf(error_, error_size_, "JSON parse error: %s", msg);
        }
    }

    void skip_ws() {
        while (pos_ < size_ &&
               (input_[pos_] == ' ' || input_[pos_] == '\t' ||
                input_[pos_] == '\n' || input_[pos_] == '\r')) {
            ++pos_;
        }
    }

    bool expect(char c) {
        skip_ws();
        if (pos_ >= size_ || input_[pos_] != c) {
            set_error("unexpected character");
            return false;
        }
        ++pos_;
        return true;
    }

    bool match_literal(const char *literal) {
        size_t len = std::strlen(literal);
        if (pos_ + len > size_) return false;
        if (std::memcmp(input_ + pos_, literal, len) != 0) return false;
        pos_ += len;
        return true;
    }

    static bool parse_hex_nibble(char c, uint8_t *out) {
        if (c >= '0' && c <= '9') { *out = static_cast<uint8_t>(c - '0'); return true; }
        if (c >= 'a' && c <= 'f') { *out = static_cast<uint8_t>(c - 'a' + 10); return true; }
        if (c >= 'A' && c <= 'F') { *out = static_cast<uint8_t>(c - 'A' + 10); return true; }
        return false;
    }

    bool parse_string(std::string &out) {
        if (!expect('"')) return false;
        out.clear();
        while (pos_ < size_) {
            char c = input_[pos_++];
            if (c == '"') return true;
            if (c == '\\') {
                if (pos_ >= size_) {
                    set_error("unterminated escape in string");
                    return false;
                }
                char esc = input_[pos_++];
                switch (esc) {
                    case '"': out += '"'; break;
                    case '\\': out += '\\'; break;
                    case '/': out += '/'; break;
                    case 'b': out += '\b'; break;
                    case 'f': out += '\f'; break;
                    case 'n': out += '\n'; break;
                    case 'r': out += '\r'; break;
                    case 't': out += '\t'; break;
                    case 'u': {
                        if (pos_ + 4 > size_) {
                            set_error("incomplete unicode escape");
                            return false;
                        }
                        uint16_t code = 0;
                        for (int i = 0; i < 4; ++i) {
                            uint8_t nibble;
                            if (!parse_hex_nibble(input_[pos_++], &nibble)) {
                                set_error("invalid unicode escape");
                                return false;
                            }
                            code = static_cast<uint16_t>((code << 4) | nibble);
                        }
                        if (code < 0x80) {
                            out += static_cast<char>(code);
                        } else if (code < 0x800) {
                            out += static_cast<char>(0xC0 | (code >> 6));
                            out += static_cast<char>(0x80 | (code & 0x3F));
                        } else {
                            out += static_cast<char>(0xE0 | (code >> 12));
                            out += static_cast<char>(0x80 | ((code >> 6) & 0x3F));
                            out += static_cast<char>(0x80 | (code & 0x3F));
                        }
                        break;
                    }
                    default: out += esc; break;
                }
            } else {
                if (static_cast<unsigned char>(c) < 0x20) {
                    set_error("unescaped control character");
                    return false;
                }
                out += c;
            }
            if (out.size() > max_string_len) {
                set_error("string exceeds maximum length");
                return false;
            }
        }
        set_error("unterminated string");
        return false;
    }

    bool parse_number(double &out) {
        size_t start = pos_;
        if (pos_ < size_ && (input_[pos_] == '-' || input_[pos_] == '+')) ++pos_;
        while (pos_ < size_ && std::isdigit(static_cast<unsigned char>(input_[pos_]))) ++pos_;
        if (pos_ < size_ && input_[pos_] == '.') {
            ++pos_;
            while (pos_ < size_ && std::isdigit(static_cast<unsigned char>(input_[pos_]))) ++pos_;
        }
        if (pos_ < size_ && (input_[pos_] == 'e' || input_[pos_] == 'E')) {
            size_t epos = pos_;
            ++pos_;
            if (pos_ < size_ && (input_[pos_] == '+' || input_[pos_] == '-')) ++pos_;
            bool has_digits = false;
            while (pos_ < size_ && std::isdigit(static_cast<unsigned char>(input_[pos_]))) {
                ++pos_;
                has_digits = true;
            }
            if (!has_digits) pos_ = epos;
        }
        if (pos_ == start) {
            set_error("expected number");
            return false;
        }
        std::string token(input_ + start, pos_ - start);
        char *endptr = nullptr;
        out = std::strtod(token.c_str(), &endptr);
        if (endptr != token.c_str() + token.size()) {
            set_error("invalid number");
            return false;
        }
        return true;
    }

    bool parse_value(json_value &out) {
        skip_ws();
        if (pos_ >= size_) {
            set_error("unexpected end of input");
            return false;
        }
        if (depth_ >= max_depth) {
            set_error("nesting too deep");
            return false;
        }
        char c = input_[pos_];
        if (c == '{') {
            ++depth_;
            bool ok = parse_object(out);
            --depth_;
            return ok;
        }
        if (c == '[') {
            ++depth_;
            bool ok = parse_array(out);
            --depth_;
            return ok;
        }
        if (c == '"') {
            out.type = json_type::string;
            return parse_string(out.string_value);
        }
        if (c == 't') {
            if (match_literal("true")) {
                out.type = json_type::boolean;
                out.bool_value = true;
                return true;
            }
            set_error("expected 'true'");
            return false;
        }
        if (c == 'f') {
            if (match_literal("false")) {
                out.type = json_type::boolean;
                out.bool_value = false;
                return true;
            }
            set_error("expected 'false'");
            return false;
        }
        if (c == 'n') {
            if (match_literal("null")) {
                out.type = json_type::null;
                return true;
            }
            set_error("expected 'null'");
            return false;
        }
        out.type = json_type::number;
        return parse_number(out.number_value);
    }

    bool parse_object(json_value &out) {
        if (!expect('{')) return false;
        out.type = json_type::object;
        out.object_value.clear();
        skip_ws();
        if (pos_ < size_ && input_[pos_] == '}') {
            expect('}');
            return true;
        }
        while (true) {
            skip_ws();
            if (pos_ >= size_ || input_[pos_] != '"') {
                set_error("expected string key in object");
                return false;
            }
            std::string key;
            if (!parse_string(key)) return false;
            skip_ws();
            if (!expect(':')) return false;
            json_value val;
            if (!parse_value(val)) return false;
            if (out.object_value.size() >= max_object_size) {
                set_error("object too large");
                return false;
            }
            out.object_value[key] = std::move(val);
            skip_ws();
            if (pos_ < size_ && input_[pos_] == ',') {
                expect(',');
                continue;
            }
            if (pos_ < size_ && input_[pos_] == '}') {
                expect('}');
                return true;
            }
            set_error("expected ',' or '}' in object");
            return false;
        }
    }

    bool parse_array(json_value &out) {
        if (!expect('[')) return false;
        out.type = json_type::array;
        out.array_value.clear();
        skip_ws();
        if (pos_ < size_ && input_[pos_] == ']') {
            expect(']');
            return true;
        }
        while (true) {
            json_value val;
            if (!parse_value(val)) return false;
            if (out.array_value.size() >= max_array_size) {
                set_error("array too large");
                return false;
            }
            out.array_value.push_back(std::move(val));
            skip_ws();
            if (pos_ < size_ && input_[pos_] == ',') {
                expect(',');
                continue;
            }
            if (pos_ < size_ && input_[pos_] == ']') {
                expect(']');
                return true;
            }
            set_error("expected ',' or ']' in array");
            return false;
        }
    }
};

class writer {
public:
    explicit writer(std::string &buffer) : buffer_(buffer) {}

    void begin_object() {
        write_comma();
        buffer_ += '{';
        needs_comma_ = false;
    }

    void end_object() {
        buffer_ += '}';
        needs_comma_ = true;
    }

    void begin_array() {
        write_comma();
        buffer_ += '[';
        needs_comma_ = false;
    }

    void end_array() {
        buffer_ += ']';
        needs_comma_ = true;
    }

    void key(const char *name) {
        write_comma();
        buffer_ += '"';
        append_escaped(name);
        buffer_ += "\":";
        needs_comma_ = false;
    }

    void string_value(const char *value) {
        write_comma();
        buffer_ += '"';
        append_escaped(value ? value : "");
        buffer_ += '"';
        needs_comma_ = true;
    }

    void number_value(double value) {
        write_comma();
        if (!std::isfinite(value)) {
            buffer_ += '0';
        } else {
            char buf[64];
            std::snprintf(buf, sizeof(buf), "%.10g", value);
            buffer_ += buf;
        }
        needs_comma_ = true;
    }

    void int_value(int value) {
        write_comma();
        char buf[32];
        std::snprintf(buf, sizeof(buf), "%d", value);
        buffer_ += buf;
        needs_comma_ = true;
    }

    void bool_value(bool value) {
        write_comma();
        buffer_ += value ? "true" : "false";
        needs_comma_ = true;
    }

    void null_value() {
        write_comma();
        buffer_ += "null";
        needs_comma_ = true;
    }

private:
    std::string &buffer_;
    bool needs_comma_ = false;

    void write_comma() {
        if (needs_comma_) buffer_ += ',';
    }

    void append_escaped(const char *s) {
        static const char hex[] = "0123456789abcdef";
        for (const char *p = s; *p; ++p) {
            unsigned char c = static_cast<unsigned char>(*p);
            switch (c) {
                case '"': buffer_ += "\\\""; break;
                case '\\': buffer_ += "\\\\"; break;
                case '\b': buffer_ += "\\b"; break;
                case '\f': buffer_ += "\\f"; break;
                case '\n': buffer_ += "\\n"; break;
                case '\r': buffer_ += "\\r"; break;
                case '\t': buffer_ += "\\t"; break;
                default:
                    if (c < 0x20) {
                        char buf[7];
                        std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                        buffer_ += buf;
                    } else {
                        buffer_ += static_cast<char>(c);
                    }
            }
        }
    }
};

} // namespace json
} // namespace solar_sim

using namespace solar_sim::json;

static void copy_string(char *dest, size_t dest_size, const char *src) {
    if (!dest || dest_size == 0) return;
    if (!src) {
        dest[0] = '\0';
        return;
    }
    size_t len = std::strlen(src);
    if (len >= dest_size) len = dest_size - 1;
    std::memcpy(dest, src, len);
    dest[len] = '\0';
}

static bool equal_case_insensitive(const char *a, const char *b) {
    if (!a || !b) return a == b;
    while (*a && *b) {
        if (std::tolower(static_cast<unsigned char>(*a)) !=
            std::tolower(static_cast<unsigned char>(*b))) {
            return false;
        }
        ++a;
        ++b;
    }
    return *a == '\0' && *b == '\0';
}

static bool guid_is_zero(const solar_guid_t *guid) {
    return !guid || (guid->id_high == 0 && guid->id_low == 0);
}

static uint64_t g_next_guid_low = 0x20000000;

static void make_guid(solar_guid_t *guid) {
    if (!guid) return;
    guid->id_high = 0;
    guid->id_low = g_next_guid_low++;
}

static bool parse_guid(const char *str, solar_guid_t *out) {
    if (!str || !out) return false;
    uint8_t bytes[16] = {0};
    size_t i = 0;
    size_t byte_count = 0;
    while (str[i] != '\0' && byte_count < 16) {
        if (str[i] == '-') { ++i; continue; }
        uint8_t hi, lo;
        if (!parser::parse_hex_nibble(str[i], &hi)) goto hash_guid;
        if (str[i + 1] == '\0' || !parser::parse_hex_nibble(str[i + 1], &lo)) goto hash_guid;
        bytes[byte_count++] = static_cast<uint8_t>((hi << 4) | lo);
        i += 2;
    }
    if (str[i] != '\0' || byte_count != 16) goto hash_guid;
    out->id_high = 0;
    for (int b = 0; b < 8; ++b) {
        out->id_high = (out->id_high << 8) | bytes[b];
    }
    out->id_low = 0;
    for (int b = 8; b < 16; ++b) {
        out->id_low = (out->id_low << 8) | bytes[b];
    }
    return true;

hash_guid:
    {
        uint8_t hash[16] = {0};
        uint32_t h1 = 0x811c9dc5u;
        uint32_t h2 = 0x84222225u;
        for (size_t k = 0; str[k] != '\0'; ++k) {
            uint8_t ch = static_cast<unsigned char>(str[k]);
            h1 ^= ch;
            h1 *= 0x01000193u;
            h2 = (h2 << 5) + h2 + ch;
            size_t idx = k % 16;
            hash[idx] ^= ch;
            hash[idx] = static_cast<uint8_t>((hash[idx] << 1) | (hash[idx] >> 7));
        }
        std::memcpy(&hash[0], &h1, sizeof(h1));
        std::memcpy(&hash[4], &h2, sizeof(h2));
        out->id_high = 0;
        for (int b = 0; b < 8; ++b) {
            out->id_high = (out->id_high << 8) | hash[b];
        }
        out->id_low = 0;
        for (int b = 8; b < 16; ++b) {
            out->id_low = (out->id_low << 8) | hash[b];
        }
    }
    return true;
}

static void guid_to_string(const solar_guid_t *guid, char *out, size_t out_size) {
    if (!out || out_size < 33) return;
    uint8_t bytes[16];
    for (int b = 0; b < 8; ++b) {
        bytes[b] = static_cast<uint8_t>((guid->id_high >> (56 - 8 * b)) & 0xFF);
    }
    for (int b = 0; b < 8; ++b) {
        bytes[8 + b] = static_cast<uint8_t>((guid->id_low >> (56 - 8 * b)) & 0xFF);
    }
    static const char hex[] = "0123456789abcdef";
    for (int i = 0; i < 16; ++i) {
        out[2 * i] = hex[bytes[i] >> 4];
        out[2 * i + 1] = hex[bytes[i] & 0xF];
    }
    out[32] = '\0';
}

static bool json_get_string(const json_value *v, const char **out) {
    if (!v || v->type != json_type::string) return false;
    if (out) *out = v->string_value.c_str();
    return true;
}

static bool json_get_double(const json_value *v, double *out) {
    if (!v) return false;
    if (v->type == json_type::number) {
        if (out) *out = v->number_value;
        return true;
    }
    if (v->type == json_type::string) {
        char *endptr = nullptr;
        double d = std::strtod(v->string_value.c_str(), &endptr);
        if (endptr == v->string_value.c_str()) return false;
        if (out) *out = d;
        return true;
    }
    return false;
}

static bool json_get_int(const json_value *v, int *out) {
    double d;
    if (!json_get_double(v, &d)) return false;
    if (out) *out = static_cast<int>(d);
    return true;
}

static bool json_get_bool(const json_value *v, bool *out) {
    if (!v || v->type != json_type::boolean) return false;
    if (out) *out = v->bool_value;
    return true;
}

static const json_value *json_find_member(const json_value *obj, const char *key) {
    if (!obj || obj->type != json_type::object || !key) return nullptr;
    auto it = obj->object_value.find(key);
    if (it != obj->object_value.end()) return &it->second;
    return nullptr;
}

static bool get_member_string(const json_value *obj, const char *key, const char **out) {
    const json_value *v = json_find_member(obj, key);
    return v && json_get_string(v, out);
}

static bool get_member_double(const json_value *obj, const char *key, double *out) {
    const json_value *v = json_find_member(obj, key);
    return v && json_get_double(v, out);
}

static bool get_member_int(const json_value *obj, const char *key, int *out) {
    const json_value *v = json_find_member(obj, key);
    return v && json_get_int(v, out);
}

static bool get_member_bool(const json_value *obj, const char *key, bool *out) {
    const json_value *v = json_find_member(obj, key);
    return v && json_get_bool(v, out);
}

static bool parse_port(const json_value *v, solar_port_t *out,
                       const solar_guid_t *owner_id,
                       solar_port_type_t default_type,
                       solar_polarity_t default_polarity) {
    if (!v || !out || !owner_id) return false;
    std::memset(out, 0, sizeof(*out));
    out->owner_id = *owner_id;
    out->type = default_type;
    out->polarity = default_polarity;
    copy_string(out->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    out->interface_type = SOLAR_CONNECTOR_UNSPECIFIED;

    const char *id_str = nullptr;
    if (get_member_string(v, "id", &id_str)) {
        parse_guid(id_str, &out->id);
    } else {
        make_guid(&out->id);
    }

    const char *type_str = nullptr;
    if (get_member_string(v, "portType", &type_str)) {
        if (equal_case_insensitive(type_str, "PVPositive")) out->type = SOLAR_PORT_PV_POSITIVE;
        else if (equal_case_insensitive(type_str, "PVNegative")) out->type = SOLAR_PORT_PV_NEGATIVE;
        else if (equal_case_insensitive(type_str, "StringInputPositive")) out->type = SOLAR_PORT_STRING_INPUT_POSITIVE;
        else if (equal_case_insensitive(type_str, "StringInputNegative")) out->type = SOLAR_PORT_STRING_INPUT_NEGATIVE;
        else if (equal_case_insensitive(type_str, "OutputPositive")) out->type = SOLAR_PORT_OUTPUT_POSITIVE;
        else if (equal_case_insensitive(type_str, "OutputNegative")) out->type = SOLAR_PORT_OUTPUT_NEGATIVE;
        else if (equal_case_insensitive(type_str, "MpptInputPositive")) out->type = SOLAR_PORT_MPPT_INPUT_POSITIVE;
        else if (equal_case_insensitive(type_str, "MpptInputNegative")) out->type = SOLAR_PORT_MPPT_INPUT_NEGATIVE;
    }

    const char *polarity_str = nullptr;
    if (get_member_string(v, "polarity", &polarity_str)) {
        if (equal_case_insensitive(polarity_str, "Positive")) out->polarity = SOLAR_POLARITY_POSITIVE;
        else if (equal_case_insensitive(polarity_str, "Negative")) out->polarity = SOLAR_POLARITY_NEGATIVE;
    }

    const char *connector = nullptr;
    if (get_member_string(v, "connectorFamily", &connector)) {
        copy_string(out->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, connector);
    }

    const char *iface = nullptr;
    if (get_member_string(v, "connectorInterface", &iface)) {
        if (equal_case_insensitive(iface, "Male")) out->interface_type = SOLAR_CONNECTOR_MALE;
        else if (equal_case_insensitive(iface, "Female")) out->interface_type = SOLAR_CONNECTOR_FEMALE;
    }

    return true;
}

static bool parse_definition(const json_value *v, solar_panel_definition_t *out) {
    if (!v || !out) return false;
    std::memset(out, 0, sizeof(*out));

    const char *id_str = nullptr;
    if (get_member_string(v, "id", &id_str)) parse_guid(id_str, &out->id);

    const char *manufacturer = nullptr;
    if (get_member_string(v, "manufacturer", &manufacturer)) {
        copy_string(out->manufacturer, SOLAR_MANUFACTURER_LEN, manufacturer);
    }
    const char *model = nullptr;
    if (get_member_string(v, "model", &model)) {
        copy_string(out->model, SOLAR_MODEL_LEN, model);
    }

    get_member_double(v, "pmaxWatts", &out->pmax_watts);
    get_member_double(v, "vmpVolts", &out->vmp_volts);
    get_member_double(v, "impAmps", &out->imp_amps);
    get_member_double(v, "vocVolts", &out->voc_volts);
    get_member_double(v, "iscAmps", &out->isc_amps);
    get_member_double(v, "widthMm", &out->width_mm);
    get_member_double(v, "heightMm", &out->height_mm);
    out->depth_mm = 35.0;
    get_member_double(v, "depthMm", &out->depth_mm);

    out->temp_coeff_voc_pct_per_c = -0.28;
    out->temp_coeff_pmax_pct_per_c = -0.35;
    get_member_double(v, "temperatureCoefficientVocPercentPerC", &out->temp_coeff_voc_pct_per_c);
    get_member_double(v, "temperatureCoefficientPmaxPercentPerC", &out->temp_coeff_pmax_pct_per_c);

    const char *connector = nullptr;
    if (get_member_string(v, "connectorFamily", &connector)) {
        copy_string(out->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, connector);
    } else {
        copy_string(out->connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
    }

    out->positive_lead_length_mm = 1000.0;
    out->negative_lead_length_mm = 1000.0;
    get_member_double(v, "positiveLeadLengthMm", &out->positive_lead_length_mm);
    get_member_double(v, "negativeLeadLengthMm", &out->negative_lead_length_mm);

    bool is_custom = false;
    if (get_member_bool(v, "isCustom", &is_custom)) out->is_custom = is_custom;

    return true;
}

static bool parse_panel(const json_value *v, solar_panel_instance_t *out) {
    if (!v || !out) return false;
    solar_guid_t id = {0, 0};
    const char *id_str = nullptr;
    if (get_member_string(v, "id", &id_str)) parse_guid(id_str, &id);
    if (guid_is_zero(&id)) make_guid(&id);

    solar_guid_t def_id = {0, 0};
    const char *def_id_str = nullptr;
    if (get_member_string(v, "definitionId", &def_id_str)) parse_guid(def_id_str, &def_id);

    double x_mm = 0.0, y_mm = 0.0;
    get_member_double(v, "positionXMm", &x_mm);
    get_member_double(v, "positionYMm", &y_mm);
    int rotation = 0;
    get_member_int(v, "rotationDegrees", &rotation);

    solar_panel_instance_init(out, &id, &def_id, x_mm, y_mm, rotation);

    const json_value *pos_port = json_find_member(v, "positivePort");
    if (pos_port) {
        parse_port(pos_port, &out->ports[0], &id, SOLAR_PORT_PV_POSITIVE, SOLAR_POLARITY_POSITIVE);
    }
    const json_value *neg_port = json_find_member(v, "negativePort");
    if (neg_port) {
        parse_port(neg_port, &out->ports[1], &id, SOLAR_PORT_PV_NEGATIVE, SOLAR_POLARITY_NEGATIVE);
    }

    if (guid_is_zero(&out->ports[0].id)) {
        out->ports[0].id = id;
        out->ports[0].id.id_low += 1;
    }
    if (guid_is_zero(&out->ports[1].id)) {
        out->ports[1].id = id;
        out->ports[1].id.id_low += 2;
    }

    const char *visual = nullptr;
    if (get_member_string(v, "visualMode", &visual)) {
        if (equal_case_insensitive(visual, "Blueprint")) out->visual_mode = SOLAR_VISUAL_BLUEPRINT;
        else if (equal_case_insensitive(visual, "ProductImage")) out->visual_mode = SOLAR_VISUAL_PRODUCT_IMAGE;
    }
    return true;
}

static bool parse_connection(const json_value *v, solar_connection_t *out) {
    if (!v || !out) return false;
    std::memset(out, 0, sizeof(*out));

    const char *start_str = nullptr;
    if (get_member_string(v, "startPortId", &start_str)) parse_guid(start_str, &out->start_port_id);
    const char *end_str = nullptr;
    if (get_member_string(v, "endPortId", &end_str)) parse_guid(end_str, &out->end_port_id);

    const json_value *wire = json_find_member(v, "wire");
    if (wire) {
        get_member_double(wire, "oneWayLengthMm", &out->length_mm);
        get_member_int(wire, "gaugeAwg", &out->gauge_awg);
        const char *wire_type = nullptr;
        if (get_member_string(wire, "wireType", &wire_type)) {
            copy_string(out->wire_type, sizeof(out->wire_type), wire_type);
        } else {
            copy_string(out->wire_type, sizeof(out->wire_type), "PV Wire");
        }
    }
    return true;
}

static bool parse_equipment(const json_value *v, solar_equipment_instance_t *out) {
    if (!v || !out) return false;
    std::memset(out, 0, sizeof(*out));

    const char *id_str = nullptr;
    if (get_member_string(v, "id", &id_str)) parse_guid(id_str, &out->id);
    if (guid_is_zero(&out->id)) make_guid(&out->id);

    const char *kind_str = nullptr;
    if (get_member_string(v, "kind", &kind_str)) {
        if (equal_case_insensitive(kind_str, "CombinerBox")) out->kind = SOLAR_EQUIPMENT_KIND_COMBINER;
        else if (equal_case_insensitive(kind_str, "PvDisconnect")) out->kind = SOLAR_EQUIPMENT_KIND_PV_DISCONNECT;
        else if (equal_case_insensitive(kind_str, "StringInverter")) out->kind = SOLAR_EQUIPMENT_KIND_STRING_INVERTER;
        else if (equal_case_insensitive(kind_str, "AcDisconnect")) out->kind = SOLAR_EQUIPMENT_KIND_AC_DISCONNECT;
        else if (equal_case_insensitive(kind_str, "AcLoadCenter")) out->kind = SOLAR_EQUIPMENT_KIND_AC_LOAD_CENTER;
        else if (equal_case_insensitive(kind_str, "Battery")) out->kind = SOLAR_EQUIPMENT_KIND_BATTERY;
        else if (equal_case_insensitive(kind_str, "BatteryDisconnect")) out->kind = SOLAR_EQUIPMENT_KIND_BATTERY_DISCONNECT;
        else if (equal_case_insensitive(kind_str, "BranchY")) out->kind = SOLAR_EQUIPMENT_KIND_BRANCH_Y;
    }

    const char *name = nullptr;
    if (get_member_string(v, "name", &name)) copy_string(out->name, SOLAR_EQUIPMENT_NAME_LEN, name);
    get_member_double(v, "positionXMm", &out->position_x_mm);
    get_member_double(v, "positionYMm", &out->position_y_mm);
    get_member_double(v, "widthMm", &out->width_mm);
    get_member_double(v, "heightMm", &out->height_mm);
    get_member_int(v, "rotationDegrees", &out->rotation_degrees);
    get_member_int(v, "stringInputCount", &out->string_input_count);
    get_member_int(v, "ratedAmps", &out->rated_amps);

    const char *series = nullptr;
    if (get_member_string(v, "catalogSeries", &series)) {
        copy_string(out->catalog_series, SOLAR_EQUIPMENT_CATALOG_SERIES_LEN, series);
    }

    const json_value *ports = json_find_member(v, "ports");
    if (ports && ports->type == json_type::array) {
        for (size_t i = 0; i < ports->array_value.size() && out->port_count < SOLAR_EQUIPMENT_MAX_PORTS; ++i) {
            solar_port_t port;
            parse_port(&ports->array_value[i], &port, &out->id,
                       SOLAR_PORT_OUTPUT_POSITIVE, SOLAR_POLARITY_POSITIVE);
            out->ports[out->port_count++] = port;
        }
    }
    return true;
}

static bool parse_surface(const json_value *v, roof_surface_t *out) {
    if (!v || !out) return false;
    std::memset(out, 0, sizeof(*out));
    const char *name = nullptr;
    if (get_member_string(v, "name", &name)) {
        copy_string(out->name, ROOF_NAME_LEN, name);
    } else {
        copy_string(out->name, ROOF_NAME_LEN, "Roof");
    }
    get_member_double(v, "setbackMm", &out->setback_mm);

    const json_value *vertices = json_find_member(v, "vertices");
    if (vertices && vertices->type == json_type::array) {
        for (size_t i = 0; i < vertices->array_value.size(); ++i) {
            const json_value *pt = &vertices->array_value[i];
            if (pt->type != json_type::object) continue;
            double x = 0.0, y = 0.0;
            get_member_double(pt, "x", &x);
            get_member_double(pt, "y", &y);
            roof_point_t p = {x, y};
            roof_surface_add_vertex(out, &p);
        }
    }
    return true;
}

static bool parse_site(const json_value *v, solar_site_conditions_t *out) {
    if (!v || !out) return false;
    const char *name = nullptr;
    if (get_member_string(v, "locationName", &name)) {
        copy_string(out->location_name, SOLAR_SITE_NAME_LEN, name);
    }
    double lat = 0.0;
    if (get_member_double(v, "latitudeDegrees", &lat)) {
        out->latitude_degrees = lat;
        out->has_latitude = true;
    }
    double lon = 0.0;
    if (get_member_double(v, "longitudeDegrees", &lon)) {
        out->longitude_degrees = lon;
        out->has_longitude = true;
    }
    get_member_double(v, "minAmbientCelsius", &out->min_ambient_celsius);
    get_member_double(v, "hotCellCelsius", &out->hot_cell_celsius);
    get_member_double(v, "peakSunHoursPerDay", &out->peak_sun_hours_per_day);
    get_member_double(v, "systemDerateFactor", &out->system_derate_factor);
    get_member_double(v, "arrayTiltDegrees", &out->array_tilt_degrees);
    get_member_double(v, "arrayAzimuthDegrees", &out->array_azimuth_degrees);
    return true;
}

static bool parse_canvas(const json_value *v, solar_project_state_t *state) {
    if (!v || !state) return false;
    get_member_bool(v, "showGrid", &state->canvas.show_grid);
    get_member_bool(v, "snapToGrid", &state->canvas.snap_to_grid);
    get_member_bool(v, "panelSnapping", &state->canvas.panel_snapping);
    get_member_bool(v, "electricalTerminalSnapping", &state->canvas.electrical_terminal_snapping);
    get_member_double(v, "panelSpacingMm", &state->canvas.panel_spacing_mm);
    get_member_double(v, "gridSizeMm", &state->canvas.grid_size_mm);
    get_member_double(v, "zoom", &state->canvas.zoom);
    get_member_double(v, "cameraXMm", &state->canvas.camera_x_mm);
    get_member_double(v, "cameraYMm", &state->canvas.camera_y_mm);
    return true;
}

static bool parse_racking(const json_value *v, solar_racking_parameters_t *out) {
    if (!v || !out) return false;
    get_member_double(v, "rafterSpacingMm", &out->rafter_spacing_mm);
    get_member_double(v, "railOverhangMm", &out->rail_overhang_mm);
    get_member_double(v, "attachmentEdgeOffsetMm", &out->attachment_edge_offset_mm);
    return true;
}

static bool project_from_json(const json_value *root, solar_project_state_t *state) {
    if (!root || !state) return false;
    if (root->type != json_type::object) return false;

    const char *name = nullptr;
    if (get_member_string(root, "name", &name)) {
        copy_string(state->name, SOLAR_PROJECT_STATE_NAME_LEN, name);
    }
    const char *id_str = nullptr;
    if (get_member_string(root, "projectId", &id_str)) {
        parse_guid(id_str, &state->project_id);
    }
    int schema = 10;
    if (get_member_int(root, "schemaVersion", &schema)) {
        state->schema_version = schema;
    }
    if (state->schema_version > 10) {
        // Future schemas are not supported by this native mirror.
    }

    const json_value *defs = json_find_member(root, "definitions");
    if (defs && defs->type == json_type::array) {
        for (size_t i = 0; i < defs->array_value.size(); ++i) {
            solar_panel_definition_t def;
            if (parse_definition(&defs->array_value[i], &def)) {
                solar_project_state_add_definition(state, &def);
            }
        }
    }

    const json_value *panels = json_find_member(root, "panels");
    if (panels && panels->type == json_type::array) {
        for (size_t i = 0; i < panels->array_value.size(); ++i) {
            solar_panel_instance_t panel;
            if (parse_panel(&panels->array_value[i], &panel)) {
                if (!solar_project_state_find_definition(state, &panel.definition_id)) {
                    solar_panel_definition_t placeholder;
                    std::memset(&placeholder, 0, sizeof(placeholder));
                    placeholder.id = panel.definition_id;
                    copy_string(placeholder.manufacturer, SOLAR_MANUFACTURER_LEN, "Unknown");
                    copy_string(placeholder.model, SOLAR_MODEL_LEN, "Placeholder");
                    placeholder.pmax_watts = 400.0;
                    placeholder.vmp_volts = 31.25;
                    placeholder.imp_amps = 12.8;
                    placeholder.voc_volts = 37.1;
                    placeholder.isc_amps = 13.5;
                    placeholder.width_mm = 1134.0;
                    placeholder.height_mm = 1722.0;
                    placeholder.depth_mm = 35.0;
                    placeholder.temp_coeff_voc_pct_per_c = -0.28;
                    placeholder.temp_coeff_pmax_pct_per_c = -0.35;
                    copy_string(placeholder.connector_family, SOLAR_CONNECTOR_FAMILY_LEN, "MC4-compatible");
                    placeholder.positive_lead_length_mm = 1000.0;
                    placeholder.negative_lead_length_mm = 1000.0;
                    solar_project_state_add_definition(state, &placeholder);
                }
                solar_electrical_graph_add_panel(&state->graph, &panel);
            }
        }
    }

    const json_value *equipment = json_find_member(root, "equipment");
    if (equipment && equipment->type == json_type::array) {
        for (size_t i = 0; i < equipment->array_value.size(); ++i) {
            solar_equipment_instance_t eq;
            if (parse_equipment(&equipment->array_value[i], &eq)) {
                solar_project_state_add_equipment(state, &eq);
            }
        }
    }

    const json_value *connections = json_find_member(root, "connections");
    if (connections && connections->type == json_type::array) {
        for (size_t i = 0; i < connections->array_value.size(); ++i) {
            solar_connection_t conn;
            if (parse_connection(&connections->array_value[i], &conn)) {
                if (!guid_is_zero(&conn.start_port_id) && !guid_is_zero(&conn.end_port_id)) {
                    solar_project_state_try_connect(
                        state, &conn.start_port_id, &conn.end_port_id,
                        conn.length_mm, conn.gauge_awg);
                }
            }
        }
    }

    const json_value *roofs = json_find_member(root, "roofs");
    if (roofs && roofs->type == json_type::array) {
        roof_document_init(&state->roof);
        for (size_t i = 0; i < roofs->array_value.size(); ++i) {
            roof_surface_t surface;
            if (parse_surface(&roofs->array_value[i], &surface)) {
                roof_document_add_surface(&state->roof, &surface);
            }
        }
    } else {
        const json_value *roof = json_find_member(root, "roof");
        if (roof && roof->type == json_type::object) {
            roof_surface_t surface;
            if (parse_surface(roof, &surface)) {
                roof_document_init(&state->roof);
                roof_document_add_surface(&state->roof, &surface);
            }
        }
    }

    const json_value *site = json_find_member(root, "site");
    if (site) parse_site(site, &state->site);
    const json_value *canvas = json_find_member(root, "canvas");
    if (canvas) parse_canvas(canvas, state);
    const json_value *racking = json_find_member(root, "racking");
    if (racking) parse_racking(racking, &state->racking);

    return true;
}

static void serialize_guid(const solar_guid_t *guid, writer &w) {
    char buf[33];
    guid_to_string(guid, buf, sizeof(buf));
    w.string_value(buf);
}

static void serialize_port(const solar_port_t *port, writer &w) {
    w.begin_object();
    w.key("id");
    serialize_guid(&port->id, w);
    w.key("portType");
    const char *type_str = "PVPositive";
    switch (port->type) {
        case SOLAR_PORT_PV_NEGATIVE: type_str = "PVNegative"; break;
        case SOLAR_PORT_STRING_INPUT_POSITIVE: type_str = "StringInputPositive"; break;
        case SOLAR_PORT_STRING_INPUT_NEGATIVE: type_str = "StringInputNegative"; break;
        case SOLAR_PORT_OUTPUT_POSITIVE: type_str = "OutputPositive"; break;
        case SOLAR_PORT_OUTPUT_NEGATIVE: type_str = "OutputNegative"; break;
        case SOLAR_PORT_MPPT_INPUT_POSITIVE: type_str = "MpptInputPositive"; break;
        case SOLAR_PORT_MPPT_INPUT_NEGATIVE: type_str = "MpptInputNegative"; break;
        default: break;
    }
    w.string_value(type_str);
    w.key("polarity");
    w.string_value(port->polarity == SOLAR_POLARITY_NEGATIVE ? "Negative" : "Positive");
    w.key("connectorFamily");
    w.string_value(port->connector_family);
    w.key("connectorInterface");
    const char *iface = "Unspecified";
    if (port->interface_type == SOLAR_CONNECTOR_MALE) iface = "Male";
    else if (port->interface_type == SOLAR_CONNECTOR_FEMALE) iface = "Female";
    w.string_value(iface);
    w.end_object();
}

static void serialize_definition(const solar_panel_definition_t *def, writer &w) {
    w.begin_object();
    w.key("id");
    serialize_guid(&def->id, w);
    w.key("manufacturer");
    w.string_value(def->manufacturer);
    w.key("model");
    w.string_value(def->model);
    w.key("pmaxWatts");
    w.number_value(def->pmax_watts);
    w.key("vmpVolts");
    w.number_value(def->vmp_volts);
    w.key("impAmps");
    w.number_value(def->imp_amps);
    w.key("vocVolts");
    w.number_value(def->voc_volts);
    w.key("iscAmps");
    w.number_value(def->isc_amps);
    w.key("widthMm");
    w.number_value(def->width_mm);
    w.key("heightMm");
    w.number_value(def->height_mm);
    w.key("depthMm");
    w.number_value(def->depth_mm);
    w.key("temperatureCoefficientVocPercentPerC");
    w.number_value(def->temp_coeff_voc_pct_per_c);
    w.key("temperatureCoefficientPmaxPercentPerC");
    w.number_value(def->temp_coeff_pmax_pct_per_c);
    w.key("connectorFamily");
    w.string_value(def->connector_family);
    w.key("positiveLeadLengthMm");
    w.number_value(def->positive_lead_length_mm);
    w.key("negativeLeadLengthMm");
    w.number_value(def->negative_lead_length_mm);
    w.key("isCustom");
    w.bool_value(def->is_custom);
    w.end_object();
}

static void serialize_panel(const solar_panel_instance_t *panel, writer &w) {
    w.begin_object();
    w.key("id");
    serialize_guid(&panel->id, w);
    w.key("definitionId");
    serialize_guid(&panel->definition_id, w);
    w.key("positionXMm");
    w.number_value(panel->position_x_mm);
    w.key("positionYMm");
    w.number_value(panel->position_y_mm);
    w.key("rotationDegrees");
    w.int_value(panel->rotation_degrees);
    const char *visual = "Simple";
    if (panel->visual_mode == SOLAR_VISUAL_BLUEPRINT) visual = "Blueprint";
    else if (panel->visual_mode == SOLAR_VISUAL_PRODUCT_IMAGE) visual = "ProductImage";
    w.key("visualMode");
    w.string_value(visual);
    w.key("positivePort");
    serialize_port(&panel->ports[0], w);
    w.key("negativePort");
    serialize_port(&panel->ports[1], w);
    w.end_object();
}

static void serialize_connection(const solar_connection_t *conn, writer &w) {
    w.begin_object();
    w.key("id");
    serialize_guid(&conn->id, w);
    w.key("startPortId");
    serialize_guid(&conn->start_port_id, w);
    w.key("endPortId");
    serialize_guid(&conn->end_port_id, w);
    w.key("wire");
    w.begin_object();
    w.key("gaugeAwg");
    w.int_value(conn->gauge_awg);
    w.key("wireType");
    w.string_value(conn->wire_type);
    w.key("material");
    w.string_value("Copper");
    w.key("color");
    w.string_value("Black");
    w.key("oneWayLengthMm");
    w.number_value(conn->length_mm);
    w.key("additionalLengthMm");
    w.number_value(0.0);
    w.key("waypoints");
    w.begin_array();
    w.end_array();
    w.end_object();
    w.end_object();
}

static void serialize_equipment(const solar_equipment_instance_t *eq, writer &w) {
    w.begin_object();
    w.key("id");
    serialize_guid(&eq->id, w);
    const char *kind = "CombinerBox";
    switch (eq->kind) {
        case SOLAR_EQUIPMENT_KIND_PV_DISCONNECT: kind = "PvDisconnect"; break;
        case SOLAR_EQUIPMENT_KIND_STRING_INVERTER: kind = "StringInverter"; break;
        case SOLAR_EQUIPMENT_KIND_AC_DISCONNECT: kind = "AcDisconnect"; break;
        case SOLAR_EQUIPMENT_KIND_AC_LOAD_CENTER: kind = "AcLoadCenter"; break;
        case SOLAR_EQUIPMENT_KIND_BATTERY: kind = "Battery"; break;
        case SOLAR_EQUIPMENT_KIND_BATTERY_DISCONNECT: kind = "BatteryDisconnect"; break;
        case SOLAR_EQUIPMENT_KIND_BRANCH_Y: kind = "BranchY"; break;
        default: break;
    }
    w.key("kind");
    w.string_value(kind);
    w.key("name");
    w.string_value(eq->name);
    w.key("positionXMm");
    w.number_value(eq->position_x_mm);
    w.key("positionYMm");
    w.number_value(eq->position_y_mm);
    w.key("rotationDegrees");
    w.int_value(eq->rotation_degrees);
    w.key("widthMm");
    w.number_value(eq->width_mm);
    w.key("heightMm");
    w.number_value(eq->height_mm);
    w.key("stringInputCount");
    w.int_value(eq->string_input_count);
    w.key("ratedAmps");
    w.int_value(eq->rated_amps);
    w.key("catalogSeries");
    w.string_value(eq->catalog_series);
    w.key("ports");
    w.begin_array();
    for (size_t i = 0; i < eq->port_count; ++i) {
        serialize_port(&eq->ports[i], w);
    }
    w.end_array();
    w.end_object();
}

static void serialize_surface(const roof_surface_t *surface, writer &w) {
    w.begin_object();
    w.key("name");
    w.string_value(surface->name);
    w.key("setbackMm");
    w.number_value(surface->setback_mm);
    w.key("isVisible");
    w.bool_value(true);
    w.key("isLocked");
    w.bool_value(surface->is_locked);
    w.key("isClosed");
    w.bool_value(true);
    w.key("enforceSetback");
    w.bool_value(true);
    w.key("enforceBoundary");
    w.bool_value(true);
    w.key("enforceObstacles");
    w.bool_value(true);
    w.key("vertices");
    w.begin_array();
    for (size_t i = 0; i < surface->vertex_count; ++i) {
        w.begin_object();
        w.key("x");
        w.number_value(surface->vertices[i].x_mm);
        w.key("y");
        w.number_value(surface->vertices[i].y_mm);
        w.end_object();
    }
    w.end_array();
    w.key("obstacles");
    w.begin_array();
    w.end_array();
    w.end_object();
}

static void serialize_site(const solar_site_conditions_t *site, writer &w) {
    w.begin_object();
    w.key("locationName");
    w.string_value(site->location_name);
    w.key("latitudeDegrees");
    w.number_value(site->has_latitude ? site->latitude_degrees : 0.0);
    w.key("longitudeDegrees");
    w.number_value(site->has_longitude ? site->longitude_degrees : 0.0);
    w.key("minAmbientCelsius");
    w.number_value(site->min_ambient_celsius);
    w.key("hotCellCelsius");
    w.number_value(site->hot_cell_celsius);
    w.key("peakSunHoursPerDay");
    w.number_value(site->peak_sun_hours_per_day);
    w.key("systemDerateFactor");
    w.number_value(site->system_derate_factor);
    w.key("arrayTiltDegrees");
    w.number_value(site->array_tilt_degrees);
    w.key("arrayAzimuthDegrees");
    w.number_value(site->array_azimuth_degrees);
    w.end_object();
}

static void serialize_canvas(const solar_project_state_t *state, writer &w) {
    w.begin_object();
    w.key("showGrid");
    w.bool_value(state->canvas.show_grid);
    w.key("snapToGrid");
    w.bool_value(state->canvas.snap_to_grid);
    w.key("panelSnapping");
    w.bool_value(state->canvas.panel_snapping);
    w.key("electricalTerminalSnapping");
    w.bool_value(state->canvas.electrical_terminal_snapping);
    w.key("panelSpacingMm");
    w.number_value(state->canvas.panel_spacing_mm);
    w.key("gridSizeMm");
    w.number_value(state->canvas.grid_size_mm);
    w.key("zoom");
    w.number_value(state->canvas.zoom);
    w.key("cameraXMm");
    w.number_value(state->canvas.camera_x_mm);
    w.key("cameraYMm");
    w.number_value(state->canvas.camera_y_mm);
    w.end_object();
}

static void serialize_racking(const solar_racking_parameters_t *racking, writer &w) {
    w.begin_object();
    w.key("rafterSpacingMm");
    w.number_value(racking->rafter_spacing_mm);
    w.key("railOverhangMm");
    w.number_value(racking->rail_overhang_mm);
    w.key("attachmentEdgeOffsetMm");
    w.number_value(racking->attachment_edge_offset_mm);
    w.end_object();
}

static void serialize_project(const solar_project_state_t *state, writer &w) {
    w.begin_object();
    w.key("schemaVersion");
    w.int_value(state->schema_version);
    w.key("projectId");
    serialize_guid(&state->project_id, w);
    w.key("name");
    w.string_value(state->name);
    w.key("lengthUnit");
    w.string_value("Meters");
    w.key("definitions");
    w.begin_array();
    for (size_t i = 0; i < state->definitions.count; ++i) {
        serialize_definition(&state->definitions.definitions[i], w);
    }
    w.end_array();
    w.key("panels");
    w.begin_array();
    for (size_t i = 0; i < state->graph.component_count; ++i) {
        if (state->graph.components[i].kind == SOLAR_COMPONENT_PANEL) {
            serialize_panel(&state->graph.components[i].data.panel, w);
        }
    }
    w.end_array();
    w.key("connections");
    w.begin_array();
    for (size_t i = 0; i < state->graph.connection_count; ++i) {
        serialize_connection(&state->graph.connections[i], w);
    }
    w.end_array();
    w.key("equipment");
    w.begin_array();
    for (const auto &eq : state->equipment) {
        serialize_equipment(&eq, w);
    }
    w.end_array();
    w.key("roofs");
    w.begin_array();
    for (size_t i = 0; i < state->roof.surface_count; ++i) {
        serialize_surface(&state->roof.surfaces[i], w);
    }
    w.end_array();
    w.key("activeRoofId");
    solar_guid_t zero = {0, 0};
    serialize_guid(&zero, w);
    w.key("site");
    serialize_site(&state->site, w);
    w.key("canvas");
    serialize_canvas(state, w);
    w.key("racking");
    serialize_racking(&state->racking, w);
    w.end_object();
}

} // namespace

extern "C" {

bool solar_project_json_parse(
    const char *json,
    size_t length,
    solar_project_state_t *state,
    char *error_buffer,
    size_t error_buffer_size) {
    if (!json || !state) {
        if (error_buffer && error_buffer_size > 0) {
            std::snprintf(error_buffer, error_buffer_size, "null input or state");
        }
        return false;
    }
    solar_project_state_init(state);
    solar_sim::json::parser p(json, length, error_buffer, error_buffer_size);
    solar_sim::json::json_value root;
    if (!p.parse(root)) return false;
    return project_from_json(&root, state);
}

bool solar_project_json_parse_bytes(
    const uint8_t *data,
    size_t size,
    solar_project_state_t *state,
    char *error_buffer,
    size_t error_buffer_size) {
    return solar_project_json_parse(
        reinterpret_cast<const char *>(data), size, state, error_buffer, error_buffer_size);
}

bool solar_project_json_serialize(
    const solar_project_state_t *state,
    char *output_buffer,
    size_t output_buffer_size,
    size_t *output_length) {
    if (!state || !output_buffer || output_buffer_size == 0) return false;
    std::string buffer;
    solar_sim::json::writer w(buffer);
    serialize_project(state, w);
    if (buffer.size() >= output_buffer_size) return false;
    std::memcpy(output_buffer, buffer.data(), buffer.size());
    output_buffer[buffer.size()] = '\0';
    if (output_length) *output_length = buffer.size();
    return true;
}

} // extern "C"
