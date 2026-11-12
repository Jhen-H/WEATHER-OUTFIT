const imageByClothingId: Record<string, string> = {
  shirt: "shirt-v1.png", tshirt: "tshirt-v1.png", "crop-top": "crop-top-v1.png", "loose-top": "loose-top-v1.png", "long-sleeve": "long-sleeve-v1.png", "sleeveless-top": "sleeveless-top-v1.png", sweatshirt: "sweatshirt-v1.png",
  "thin-jacket": "thin-jacket-v1.png", "knit-cardigan": "cardigan-v1.png", blazer: "blazer-v1.png", "denim-jacket": "denim-jacket-v1.png", hoodie: "hoodie-v1.png", puffer: "puffer-v1.png", "long-coat": "long-coat-v1.png",
  "denim-shorts": "denim-shorts-v1.png", shorts: "shorts-v1.png", "five-shorts": "five-shorts-v1.png", "denim-pants": "jeans-v1.png", "long-pants": "long-pants-v1.png", "pleated-skirt": "pleated-skirt-v1.png", "long-skirt": "long-skirt-v1.png", "wide-pants": "wide-pants-v1.png",
  dress: "dress-v1.png", overalls: "overalls-v1.png", jumpsuit: "jumpsuit-v1.png",
};

export function ClothingIcon({ id }: { id: string }) {
  const filename = imageByClothingId[id] ?? "tshirt-v1.png";
  const isPreferenceAsset = filename.endsWith("-v1.png");
  return <img src={`${import.meta.env.BASE_URL}${isPreferenceAsset ? "preference-clothing" : "outfits"}/${filename}`} alt="" aria-hidden="true" />;
}
